using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sellora.OrderService.Application.Dependencies;
using Sellora.OrderService.Application.Events;
using Sellora.OrderService.Application.Identity;
using Sellora.OrderService.Application.Orders;
using Sellora.OrderService.Application.VanReturns;
using Sellora.OrderService.Domain.Entities;
using Sellora.OrderService.Domain.Orders;
using Sellora.OrderService.Domain.Tenancy;
using Sellora.OrderService.Domain.VanReturns;
using Sellora.OrderService.Infrastructure.Dependencies;
using Sellora.OrderService.Infrastructure.Persistence;

namespace Sellora.OrderService.Infrastructure.VanReturns;

/// <summary>
/// US-E4-6: end-of-route van returns.
///
/// Declare (rep): the declared quantities are checked against what the
/// rep's van can hand over right now — on hand minus anything held for an
/// unfinished cash sale — so a rep can never return stock they do not have.
/// An over-return would create stock out of nothing and hide a shortage.
///
/// Accept (the rep's own agency operator): the counted quantities are
/// recorded next to the declared ones, the van is checked again (the rep
/// may have sold some since declaring), and VanStockReturned is written in
/// the same SaveChanges. Inventory moves the counted stock when it consumes
/// the event, so the acceptance and the transfer cannot drift apart.
/// </summary>
public sealed class VanReturnService : IVanReturnService
{
    private readonly OrderDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUserContext _caller;
    private readonly IInventoryClient _inventory;
    private readonly ICatalogClient _catalog;
    private readonly TimeProvider _clock;
    private readonly IVanReturnEventOutbox _events;
    private readonly ILogger<VanReturnService> _logger;

    public VanReturnService(
        OrderDbContext db,
        ITenantContext tenant,
        ICurrentUserContext caller,
        IInventoryClient inventory,
        ICatalogClient catalog,
        TimeProvider clock,
        IVanReturnEventOutbox events,
        ILogger<VanReturnService> logger)
    {
        _db = db;
        _tenant = tenant;
        _caller = caller;
        _inventory = inventory;
        _catalog = catalog;
        _clock = clock;
        _events = events;
        _logger = logger;
    }

    public async Task<VanReturnResult> DeclareAsync(DeclareVanReturnRequest request, CancellationToken cancellationToken)
    {
        if (_tenant.CompanyId is not { } companyId)
        {
            return VanReturnResult.Failed(VanReturnOutcome.TenantNotAvailable, "A valid company identifier was not found in the access token.");
        }

        if (_caller.SalesRepId is not { } salesRepId || string.IsNullOrWhiteSpace(_caller.Subject))
        {
            return VanReturnResult.Failed(VanReturnOutcome.CallerNotPermitted, "The access token does not identify a sales rep.");
        }

        if (_caller.AgencyId is not { } agencyId)
        {
            return VanReturnResult.Failed(
                VanReturnOutcome.CallerNotPermitted,
                "You are not assigned to an agency, so there is no agency to return stock to.");
        }

        var lines = (request.Lines ?? Array.Empty<DeclareVanReturnLine>())
            .Select(line => new NewVanReturnLine(line.ProductId, null, line.Quantity))
            .ToList();

        try
        {
            VanReturn.ValidateDeclaredLines(lines);
        }
        catch (VanReturnRuleException exception)
        {
            return VanReturnResult.Failed(VanReturnOutcome.InvalidRequest, exception.Message);
        }

        try
        {
            var vanOwnerId = await _inventory.FindVanOwnerAsync(salesRepId, cancellationToken);

            if (vanOwnerId is null)
            {
                return VanReturnResult.Failed(
                    VanReturnOutcome.NoVanStock,
                    "You have no van stock in Inventory, so there is nothing to return.");
            }

            var names = await ProductNamesAsync(companyId, lines.Select(line => line.ProductId).ToList(), cancellationToken);
            var named = lines.Select(line => line with { ProductName = names.GetValueOrDefault(line.ProductId) }).ToList();

            var shortages = await VanShortagesAsync(
                vanOwnerId.Value,
                named.Select(line => (line.ProductId, line.ProductName, line.DeclaredQuantity)).ToList(),
                cancellationToken);

            if (shortages.Count > 0)
            {
                // Scenario 2: nothing is created.
                return new VanReturnResult(
                    VanReturnOutcome.ExceedsVanStock,
                    Message: DescribeShortages(shortages, "Your van holds"),
                    Shortages: shortages);
            }

            var vanReturn = VanReturn.Declare(
                companyId, salesRepId, _caller.DisplayName, agencyId, vanOwnerId.Value,
                _caller.Subject!, named, _clock.GetUtcNow());

            _db.VanReturns.Add(vanReturn);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Van return {ReturnReference} declared by rep {SalesRepId} for agency {AgencyId}: {Units} units over {Lines} products",
                vanReturn.ReturnReference, salesRepId, agencyId, vanReturn.TotalDeclared, vanReturn.Lines.Count);

            return VanReturnResult.Done(VanReturnResponse.From(vanReturn), changed: true);
        }
        catch (DependencyUnavailableException exception)
        {
            return new VanReturnResult(
                VanReturnOutcome.DependencyUnavailable, Message: exception.Message, Dependency: exception.Dependency.ToString());
        }
        catch (DependencyRejectedException exception)
        {
            return new VanReturnResult(
                VanReturnOutcome.DependencyUnavailable, Message: exception.Message, Dependency: exception.Dependency.ToString());
        }
    }

    public async Task<VanReturnResult> AcceptAsync(
        Guid vanReturnId,
        AcceptVanReturnRequest request,
        CancellationToken cancellationToken)
    {
        if (_tenant.CompanyId is null)
        {
            return VanReturnResult.Failed(VanReturnOutcome.TenantNotAvailable, "A valid company identifier was not found in the access token.");
        }

        if (_caller.AgencyId is not { } agencyId || string.IsNullOrWhiteSpace(_caller.Subject))
        {
            return VanReturnResult.Failed(VanReturnOutcome.CallerNotPermitted, "The access token does not identify an agency operator.");
        }

        // Another agency's return is indistinguishable from a missing one.
        var vanReturn = await _db.VanReturns
            .Include(candidate => candidate.Lines)
            .SingleOrDefaultAsync(
                candidate => candidate.VanReturnId == vanReturnId && candidate.AgencyId == agencyId,
                cancellationToken);

        if (vanReturn is null)
        {
            return VanReturnResult.Failed(VanReturnOutcome.NotFound, $"No van return {vanReturnId} is waiting for your agency.");
        }

        var counts = (request.Lines ?? Array.Empty<AcceptVanReturnLine>())
            .Select(line => new VanReturnCount(line.ProductId, line.CountedQuantity))
            .ToList();

        var now = _clock.GetUtcNow();
        bool changed;

        try
        {
            changed = vanReturn.Accept(agencyId, _caller.Subject!, counts, request.Note, now);
        }
        catch (VanReturnRuleException exception)
        {
            return VanReturnResult.Failed(
                exception.Failure switch
                {
                    VanReturnFailure.NotVisible => VanReturnOutcome.NotFound,
                    VanReturnFailure.AlreadyAccepted => VanReturnOutcome.Conflict,
                    _ => VanReturnOutcome.InvalidRequest
                },
                exception.Message);
        }

        if (!changed)
        {
            return VanReturnResult.Done(VanReturnResponse.From(vanReturn), changed: false);
        }

        try
        {
            // The van must still hold what is about to move: the rep may
            // have sold some since declaring. Checked now, not at transfer,
            // so the operator hears it instead of the event being refused.
            var shortages = await VanShortagesAsync(
                vanReturn.VanInventoryOwnerId,
                vanReturn.Lines
                    .Where(line => line.CountedQuantity > 0)
                    .Select(line => (line.ProductId, line.ProductNameSnapshot, line.CountedQuantity!.Value))
                    .ToList(),
                cancellationToken);

            if (shortages.Count > 0)
            {
                return new VanReturnResult(
                    VanReturnOutcome.ExceedsVanStock,
                    Message: DescribeShortages(shortages, "The rep's van now holds") +
                        " Stock may have been sold since the return was declared; ask the rep to declare again.",
                    Shortages: shortages);
            }
        }
        catch (DependencyUnavailableException exception)
        {
            return new VanReturnResult(
                VanReturnOutcome.DependencyUnavailable, Message: exception.Message, Dependency: exception.Dependency.ToString());
        }
        catch (DependencyRejectedException exception)
        {
            return new VanReturnResult(
                VanReturnOutcome.DependencyUnavailable, Message: exception.Message, Dependency: exception.Dependency.ToString());
        }

        _events.VanStockReturned(vanReturn, SelloraRoles.AgencyOperator, now);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return VanReturnResult.Failed(
                VanReturnOutcome.Conflict,
                $"Van return {vanReturn.ReturnReference} was accepted by someone else a moment ago. Reload it.");
        }

        _logger.LogInformation(
            "Van return {ReturnReference} accepted by agency {AgencyId}: declared {Declared}, counted {Counted}, variance {Variance}",
            vanReturn.ReturnReference, agencyId, vanReturn.TotalDeclared, vanReturn.TotalCounted, vanReturn.TotalVariance);

        return VanReturnResult.Done(VanReturnResponse.From(vanReturn), changed: true);
    }

    public async Task<PagedResponse<VanReturnSummaryResponse>> ListAsync(
        VanReturnListQuery query,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var scoped = Scoped(_db.VanReturns.AsNoTracking());

        if (Enum.TryParse<VanReturnStatus>(query.Status, ignoreCase: true, out var status) && Enum.IsDefined(status))
        {
            scoped = scoped.Where(vanReturn => vanReturn.Status == status);
        }

        var totalCount = await scoped.CountAsync(cancellationToken);

        var items = await scoped
            .OrderByDescending(vanReturn => vanReturn.DeclaredAt)
            .ThenBy(vanReturn => vanReturn.ReturnReference)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(vanReturn => new VanReturnSummaryResponse(
                vanReturn.VanReturnId,
                vanReturn.ReturnReference,
                vanReturn.Status.ToString(),
                vanReturn.SalesRepId,
                vanReturn.SalesRepName,
                vanReturn.AgencyId,
                vanReturn.DeclaredAt,
                vanReturn.AcceptedAt,
                vanReturn.Lines.Count,
                vanReturn.Lines.Sum(line => line.DeclaredQuantity),
                vanReturn.Status == VanReturnStatus.Accepted
                    ? (int?)vanReturn.Lines.Sum(line => line.CountedQuantity ?? 0)
                    : null,
                vanReturn.Status == VanReturnStatus.Accepted
                    ? (int?)vanReturn.Lines.Sum(line => line.Variance ?? 0)
                    : null))
            .ToListAsync(cancellationToken);

        return new PagedResponse<VanReturnSummaryResponse>(items, page, pageSize, totalCount);
    }

    public async Task<VanReturnResponse?> GetAsync(Guid vanReturnId, CancellationToken cancellationToken)
    {
        var vanReturn = await Scoped(_db.VanReturns.AsNoTracking().Include(candidate => candidate.Lines))
            .SingleOrDefaultAsync(candidate => candidate.VanReturnId == vanReturnId, cancellationToken);

        return vanReturn is null ? null : VanReturnResponse.From(vanReturn);
    }

    /// <summary>A rep sees their own returns, an operator their agency's, an admin all; nobody else any.</summary>
    private IQueryable<VanReturn> Scoped(IQueryable<VanReturn> query)
    {
        switch (_caller.Role)
        {
            case SelloraRoles.CompanyAdmin:
                return query;
            case SelloraRoles.SalesRep when _caller.SalesRepId is { } salesRepId:
                return query.Where(vanReturn => vanReturn.SalesRepId == salesRepId);
            case SelloraRoles.AgencyOperator when _caller.AgencyId is { } agencyId:
                return query.Where(vanReturn => vanReturn.AgencyId == agencyId);
            default:
                return query.Where(_ => false);
        }
    }

    private async Task<IReadOnlyList<VanStockShortage>> VanShortagesAsync(
        Guid vanOwnerId,
        IReadOnlyList<(Guid ProductId, string? Name, int Quantity)> lines,
        CancellationToken cancellationToken)
    {
        if (lines.Count == 0)
        {
            return Array.Empty<VanStockShortage>();
        }

        var availability = await _inventory.CheckAvailabilityAsync(
            vanOwnerId,
            lines.Select(line => new BasketLine(line.ProductId, line.Quantity)).ToList(),
            cancellationToken);

        var heldByProduct = availability
            .GroupBy(item => item.ProductId)
            .ToDictionary(group => group.Key, group => group.Max(item => item.AvailableQuantity));

        // A product Inventory does not report is one the van does not hold.
        return lines
            .Select(line => new VanStockShortage(
                line.ProductId, line.Name, line.Quantity, heldByProduct.GetValueOrDefault(line.ProductId)))
            .Where(shortage => shortage.HeldQuantity < shortage.RequestedQuantity)
            .ToList();
    }

    private async Task<Dictionary<Guid, string?>> ProductNamesAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken)
    {
        // Names are only for the acceptance screen; a product the catalogue
        // no longer lists can still be returned.
        var resolved = await _catalog.ResolveProductsAsync(companyId, productIds, cancellationToken);
        return resolved.Items
            .GroupBy(product => product.ProductId)
            .ToDictionary(group => group.Key, group => group.First().Name);
    }

    private static string DescribeShortages(IReadOnlyList<VanStockShortage> shortages, string lead) =>
        string.Join(
            " ",
            shortages.Select(shortage =>
                $"{lead} only {shortage.HeldQuantity} {Units(shortage.HeldQuantity)} of " +
                $"{shortage.ProductName ?? shortage.ProductId.ToString()}; {shortage.RequestedQuantity} cannot be returned."));

    private static string Units(int quantity) => quantity == 1 ? "unit" : "units";
}
