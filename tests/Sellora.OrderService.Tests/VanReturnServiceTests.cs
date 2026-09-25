using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sellora.OrderService.Application.Identity;
using Sellora.OrderService.Application.VanReturns;
using Sellora.OrderService.Domain.Entities;
using Sellora.OrderService.Domain.VanReturns;
using Sellora.OrderService.Infrastructure.Outbox;
using Sellora.OrderService.Infrastructure.Persistence;
using Sellora.OrderService.Infrastructure.VanReturns;

namespace Sellora.OrderService.Tests;

/// <summary>US-E4-6 acceptance scenarios against a real database, with fake dependencies.</summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class VanReturnServiceTests
{
    private const string Correlation = "van-return-correlation";

    private readonly PostgreSqlFixture _fixture;
    private readonly Guid _companyId = Guid.NewGuid();
    private readonly Guid _repId = Guid.NewGuid();
    private readonly Guid _agencyId = Guid.NewGuid();
    private readonly Guid _vanOwnerId = Guid.NewGuid();
    private readonly FakeInventory _inventory = new();
    private readonly FakeCatalog _catalog = new();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero));
    private readonly Guid _soap;

    public VanReturnServiceTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        _inventory.VanOwnerId = _vanOwnerId;
        _soap = _catalog.Add("Sunlight Soap 100g", 120m).ProductId;
        _inventory.Available[_soap] = 30; // Scenarios 1 and 2: the van holds 30.
    }

    private CallerStub Rep => new() { Subject = "rep-sub", Role = SelloraRoles.SalesRep, SalesRepId = _repId, AgencyId = _agencyId };

    private CallerStub Operator(Guid? agencyId = null) =>
        new() { Subject = "op-sub", Role = SelloraRoles.AgencyOperator, AgencyId = agencyId ?? _agencyId };

    private VanReturnService Service(OrderDbContext db, CallerStub caller) => new(
        db,
        new TenantStub(_companyId),
        caller,
        _inventory,
        _catalog,
        _clock,
        new VanReturnEventOutbox(new EntityFrameworkOutboxWriter(db), new FixedCorrelation(Correlation)),
        NullLogger<VanReturnService>.Instance);

    private async Task<VanReturnResult> DeclareAsync(int quantity)
    {
        await using var db = _fixture.CreateContext(_companyId);
        return await Service(db, Rep).DeclareAsync(
            new DeclareVanReturnRequest(new[] { new DeclareVanReturnLine(_soap, quantity) }), CancellationToken.None);
    }

    private async Task<VanReturnResult> AcceptAsync(Guid vanReturnId, int counted, Guid? agencyId = null)
    {
        await using var db = _fixture.CreateContext(_companyId);
        return await Service(db, Operator(agencyId)).AcceptAsync(
            vanReturnId,
            new AcceptVanReturnRequest(new[] { new AcceptVanReturnLine(_soap, counted) }, null),
            CancellationToken.None);
    }

    private async Task<List<OutboxMessage>> EventsAsync(Guid vanReturnId)
    {
        await using var db = _fixture.CreateContext(_companyId);
        return await db.OutboxMessages.Where(message => message.AggregateId == vanReturnId).ToListAsync();
    }

    // Scenario 1.
    [Fact]
    public async Task A_return_within_van_stock_is_declared_and_listed_for_the_agency()
    {
        var result = await DeclareAsync(12);

        Assert.Equal(VanReturnOutcome.Succeeded, result.Outcome);
        Assert.Equal("Declared", result.VanReturn!.Status);
        var line = Assert.Single(result.VanReturn.Lines);
        Assert.Equal(12, line.DeclaredQuantity);
        Assert.Equal("Sunlight Soap 100g", line.ProductName);
        Assert.Equal(_vanOwnerId, _inventory.AvailabilityChecks.Single().OwnerId);

        await using var db = _fixture.CreateContext(_companyId);
        var pending = await Service(db, Operator()).ListAsync(
            new VanReturnListQuery(1, 20, "Declared"), CancellationToken.None);
        Assert.Contains(pending.Items, item => item.VanReturnId == result.VanReturn.VanReturnId);

        var otherAgency = await Service(db, Operator(Guid.NewGuid())).ListAsync(
            new VanReturnListQuery(1, 20, "Declared"), CancellationToken.None);
        Assert.DoesNotContain(otherAgency.Items, item => item.VanReturnId == result.VanReturn.VanReturnId);
    }

    // Scenario 2.
    [Fact]
    public async Task Declaring_more_than_the_van_holds_is_refused_and_creates_nothing()
    {
        var result = await DeclareAsync(40);

        Assert.Equal(VanReturnOutcome.ExceedsVanStock, result.Outcome);
        Assert.Contains("only 30 units", result.Message);
        var shortage = Assert.Single(result.Shortages!);
        Assert.Equal(40, shortage.RequestedQuantity);
        Assert.Equal(30, shortage.HeldQuantity);

        await using var db = _fixture.CreateContext(_companyId);
        Assert.False(await db.VanReturns.AnyAsync(vanReturn => vanReturn.SalesRepId == _repId));
    }

    [Fact]
    public async Task A_product_the_van_does_not_hold_cannot_be_returned()
    {
        await using var db = _fixture.CreateContext(_companyId);
        var result = await Service(db, Rep).DeclareAsync(
            new DeclareVanReturnRequest(new[] { new DeclareVanReturnLine(Guid.NewGuid(), 1) }), CancellationToken.None);

        Assert.Equal(VanReturnOutcome.ExceedsVanStock, result.Outcome);
        Assert.Contains("only 0 units", result.Message);
    }

    [Fact]
    public async Task A_rep_with_no_van_stock_owner_is_told_so()
    {
        _inventory.VanOwnerId = null;

        var result = await DeclareAsync(1);

        Assert.Equal(VanReturnOutcome.NoVanStock, result.Outcome);
    }

    // Scenario 3.
    [Fact]
    public async Task Accepting_ten_of_twelve_records_the_variance_and_publishes_only_ten()
    {
        var declared = (await DeclareAsync(12)).VanReturn!;

        var result = await AcceptAsync(declared.VanReturnId, 10);

        Assert.Equal(VanReturnOutcome.Succeeded, result.Outcome);
        var line = Assert.Single(result.VanReturn!.Lines);
        Assert.Equal(12, line.DeclaredQuantity);
        Assert.Equal(10, line.CountedQuantity);
        Assert.Equal(2, line.Variance);
        Assert.Equal(2, result.VanReturn.TotalVariance);

        await using (var db = _fixture.CreateContext(_companyId))
        {
            var stored = await db.VanReturns.Include(vanReturn => vanReturn.Lines)
                .SingleAsync(vanReturn => vanReturn.VanReturnId == declared.VanReturnId);
            Assert.Equal(VanReturnStatus.Accepted, stored.Status);
            Assert.Equal(2, stored.Lines.Single().Variance);
            Assert.Equal("op-sub", stored.AcceptedBy);
        }

        var message = Assert.Single(await EventsAsync(declared.VanReturnId));
        Assert.Equal("VanStockReturned", message.EventType);
        Assert.Equal(declared.ReturnReference, message.MessageKey);
        Assert.Equal(Correlation, message.CorrelationId);

        var payload = JsonDocument.Parse(message.Payload).RootElement;
        var eventLine = payload.GetProperty("lines")[0];
        Assert.Equal(10, eventLine.GetProperty("acceptedQuantity").GetInt32());
        Assert.Equal(12, eventLine.GetProperty("declaredQuantity").GetInt32());
        Assert.Equal(2, eventLine.GetProperty("variance").GetInt32());
        Assert.Equal(_vanOwnerId, payload.GetProperty("vanInventoryOwnerId").GetGuid());
        Assert.Equal(_agencyId, payload.GetProperty("agencyId").GetGuid());
    }

    [Fact]
    public async Task Only_the_reps_own_agency_operator_can_accept()
    {
        var declared = (await DeclareAsync(12)).VanReturn!;

        var result = await AcceptAsync(declared.VanReturnId, 12, agencyId: Guid.NewGuid());

        Assert.Equal(VanReturnOutcome.NotFound, result.Outcome);
        Assert.Empty(await EventsAsync(declared.VanReturnId));
    }

    [Fact]
    public async Task Acceptance_is_refused_when_the_van_no_longer_holds_the_counted_stock()
    {
        var declared = (await DeclareAsync(12)).VanReturn!;
        _inventory.Available[_soap] = 5; // the rep sold some after declaring

        var result = await AcceptAsync(declared.VanReturnId, 10);

        Assert.Equal(VanReturnOutcome.ExceedsVanStock, result.Outcome);
        Assert.Contains("declare again", result.Message);
        Assert.Empty(await EventsAsync(declared.VanReturnId));
    }

    [Fact]
    public async Task Repeating_an_acceptance_publishes_nothing_more()
    {
        var declared = (await DeclareAsync(12)).VanReturn!;
        await AcceptAsync(declared.VanReturnId, 12);

        var repeat = await AcceptAsync(declared.VanReturnId, 12);

        Assert.Equal(VanReturnOutcome.Succeeded, repeat.Outcome);
        Assert.False(repeat.Changed);
        Assert.Single(await EventsAsync(declared.VanReturnId));
    }

    // Inventory's consumer record for VanStockReturned, copied from
    // sellora-inventory Application/Events/VanStockReturnedEvent.cs.
    private sealed record InventoryVanStockReturned(
        Guid EventId, string EventType, string SchemaVersion, Guid CompanyId, Guid EntityId,
        string ReturnReference, Guid SalesRepId, Guid AgencyId, Guid VanInventoryOwnerId,
        IReadOnlyCollection<InventoryVanStockReturnedLine> Lines, DateTimeOffset AcceptedAt, string CorrelationId);

    private sealed record InventoryVanStockReturnedLine(Guid ProductId, int AcceptedQuantity);

    [Fact]
    public async Task The_event_matches_inventorys_consumer_contract()
    {
        var declared = (await DeclareAsync(30)).VanReturn!;
        await AcceptAsync(declared.VanReturnId, 30);

        var message = Assert.Single(await EventsAsync(declared.VanReturnId));
        var consumed = JsonSerializer.Deserialize<InventoryVanStockReturned>(
            message.Payload, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.Equal(message.OutboxId, consumed.EventId);
        Assert.Equal("VanStockReturned", consumed.EventType);
        Assert.Equal("1.0", consumed.SchemaVersion);
        Assert.Equal(_companyId, consumed.CompanyId);
        Assert.Equal(declared.VanReturnId, consumed.EntityId);
        Assert.Equal(_repId, consumed.SalesRepId);
        Assert.Equal(_vanOwnerId, consumed.VanInventoryOwnerId);
        Assert.Equal(30, Assert.Single(consumed.Lines).AcceptedQuantity);
    }
}
