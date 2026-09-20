using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Sellora.OrderService.Application.Identity;
using Sellora.OrderService.Application.Orders;
using Sellora.OrderService.Domain.Entities;
using Sellora.OrderService.Domain.Orders;
using Sellora.OrderService.Domain.Tenancy;
using Sellora.OrderService.Infrastructure.Persistence;
using Sellora.OrderService.Infrastructure.Persistence.Configurations;

namespace Sellora.OrderService.Infrastructure.Orders;

public sealed class OrderCreationService : IOrderCreationService
{
    // A reference collision needs ~1 in 594M odds per day per company;
    // three attempts is ample, and the unique index is the real guarantee.
    private const int MaxReferenceAttempts = 3;

    private readonly OrderDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUserContext _caller;
    private readonly TimeProvider _clock;
    private readonly ILogger<OrderCreationService> _logger;

    public OrderCreationService(
        OrderDbContext db,
        ITenantContext tenant,
        ICurrentUserContext caller,
        TimeProvider clock,
        ILogger<OrderCreationService> logger)
    {
        _db = db;
        _tenant = tenant;
        _caller = caller;
        _clock = clock;
        _logger = logger;
    }

    public async Task<CreateOrderResult> CreateAsync(
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (_tenant.CompanyId is not { } companyId)
        {
            return CreateOrderResult.Failed(
                CreateOrderOutcome.TenantNotAvailable,
                "A valid company identifier was not found in the access token.");
        }

        // The rep placing the order is the caller, never a body field.
        if (_caller.SalesRepId is not { } salesRepId)
        {
            return CreateOrderResult.Failed(
                CreateOrderOutcome.CallerNotSalesRep,
                "The access token does not identify a sales rep.");
        }

        for (var attempt = 1; ; attempt++)
        {
            Order order;
            var orderDate = _clock.GetUtcNow();

            try
            {
                order = Order.Create(
                    companyId,
                    request.ShopId,
                    salesRepId,
                    request.AgencyId,
                    request.TerritoryId,
                    request.ProvinceId,
                    OrderReferenceGenerator.Generate(orderDate),
                    orderDate,
                    request.Lines);
            }
            catch (OrderRuleViolationException exception)
            {
                return CreateOrderResult.Failed(
                    CreateOrderOutcome.InvalidRequest,
                    exception.Message);
            }

            _db.Orders.Add(order);

            try
            {
                await _db.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Order {OrderReference} ({OrderId}) created by rep {SalesRepId} for shop {ShopId} in company {CompanyId}",
                    order.OrderReference, order.OrderId, salesRepId, order.ShopId, companyId);

                return CreateOrderResult.Created(OrderResponse.From(order));
            }
            catch (DbUpdateException exception)
                when (IsReferenceCollision(exception) && attempt < MaxReferenceAttempts)
            {
                _logger.LogWarning(
                    "Order reference {OrderReference} collided; retrying (attempt {Attempt})",
                    order.OrderReference, attempt);

                _db.ChangeTracker.Clear();
            }
        }
    }

    private static bool IsReferenceCollision(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: OrderConfiguration.ReferenceUniqueIndex
        };
}
