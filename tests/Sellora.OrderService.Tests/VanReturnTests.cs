using Sellora.OrderService.Domain.Entities;
using Sellora.OrderService.Domain.VanReturns;

namespace Sellora.OrderService.Tests;

/// <summary>US-E4-6: the van return aggregate, no database.</summary>
public sealed class VanReturnTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _agencyId = Guid.NewGuid();
    private readonly Guid _soap = Guid.NewGuid();
    private readonly Guid _tea = Guid.NewGuid();

    private VanReturn Declared(int soap = 12, int tea = 5) => VanReturn.Declare(
        Guid.NewGuid(), Guid.NewGuid(), "Ruwan Dias", _agencyId, Guid.NewGuid(), "rep-sub",
        new[] { new NewVanReturnLine(_soap, "Soap", soap), new NewVanReturnLine(_tea, "Tea", tea) },
        Now);

    [Fact]
    public void A_declared_return_holds_its_lines_and_a_readable_reference()
    {
        var vanReturn = Declared();

        Assert.Equal(VanReturnStatus.Declared, vanReturn.Status);
        Assert.Matches("^VR-260925-[23456789ABCDEFGHJKMNPQRSTWXYZ]{6}$", vanReturn.ReturnReference);
        Assert.Equal(17, vanReturn.TotalDeclared);
        Assert.Null(vanReturn.TotalCounted);
        Assert.All(vanReturn.Lines, line => Assert.Null(line.CountedQuantity));
    }

    // Scenario 3 on the aggregate.
    [Fact]
    public void Accepting_a_lower_count_records_both_figures_and_the_variance()
    {
        var vanReturn = Declared();

        var changed = vanReturn.Accept(
            _agencyId, "op-sub",
            new[] { new VanReturnCount(_soap, 10), new VanReturnCount(_tea, 5) },
            "Two soaps crushed", Now.AddHours(1));

        Assert.True(changed);
        Assert.Equal(VanReturnStatus.Accepted, vanReturn.Status);
        var soap = vanReturn.Lines.Single(line => line.ProductId == _soap);
        Assert.Equal(12, soap.DeclaredQuantity);
        Assert.Equal(10, soap.CountedQuantity);
        Assert.Equal(2, soap.Variance);
        Assert.Equal(0, vanReturn.Lines.Single(line => line.ProductId == _tea).Variance);
        Assert.Equal(15, vanReturn.TotalCounted);
        Assert.Equal(2, vanReturn.TotalVariance);
        Assert.Equal("op-sub", vanReturn.AcceptedBy);
        Assert.Equal("Two soaps crushed", vanReturn.AcceptanceNote);
    }

    [Fact]
    public void A_count_above_the_declared_quantity_is_refused()
    {
        var vanReturn = Declared();

        var error = Assert.Throws<VanReturnRuleException>(() => vanReturn.Accept(
            _agencyId, "op-sub", new[] { new VanReturnCount(_soap, 13), new VanReturnCount(_tea, 5) }, null, Now));

        Assert.Equal(VanReturnFailure.InvalidRequest, error.Failure);
        Assert.Contains("declared only 12", error.Message);
        Assert.Equal(VanReturnStatus.Declared, vanReturn.Status);
    }

    [Fact]
    public void Every_declared_product_must_be_counted()
    {
        var vanReturn = Declared();

        var error = Assert.Throws<VanReturnRuleException>(() => vanReturn.Accept(
            _agencyId, "op-sub", new[] { new VanReturnCount(_soap, 12) }, null, Now));

        Assert.Contains("Tea", error.Message);
    }

    [Fact]
    public void A_negative_or_unknown_count_is_refused()
    {
        var vanReturn = Declared();

        Assert.Throws<VanReturnRuleException>(() => vanReturn.Accept(
            _agencyId, "op-sub", new[] { new VanReturnCount(_soap, -1), new VanReturnCount(_tea, 5) }, null, Now));
        Assert.Throws<VanReturnRuleException>(() => vanReturn.Accept(
            _agencyId, "op-sub",
            new[] { new VanReturnCount(_soap, 1), new VanReturnCount(_tea, 5), new VanReturnCount(Guid.NewGuid(), 1) },
            null, Now));
    }

    [Fact]
    public void Another_agency_cannot_accept()
    {
        var vanReturn = Declared();

        var error = Assert.Throws<VanReturnRuleException>(() => vanReturn.Accept(
            Guid.NewGuid(), "op-sub", new[] { new VanReturnCount(_soap, 12), new VanReturnCount(_tea, 5) }, null, Now));

        Assert.Equal(VanReturnFailure.NotVisible, error.Failure);
    }

    [Fact]
    public void Repeating_the_same_counts_changes_nothing_but_different_counts_conflict()
    {
        var vanReturn = Declared();
        var counts = new[] { new VanReturnCount(_soap, 10), new VanReturnCount(_tea, 5) };
        vanReturn.Accept(_agencyId, "op-sub", counts, null, Now);

        Assert.False(vanReturn.Accept(_agencyId, "op-sub", counts, null, Now.AddMinutes(1)));

        var error = Assert.Throws<VanReturnRuleException>(() => vanReturn.Accept(
            _agencyId, "op-sub", new[] { new VanReturnCount(_soap, 12), new VanReturnCount(_tea, 5) }, null, Now));
        Assert.Equal(VanReturnFailure.AlreadyAccepted, error.Failure);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(100_001)]
    public void A_declared_quantity_outside_the_limits_is_refused(int quantity)
    {
        Assert.Throws<VanReturnRuleException>(() => Declared(soap: quantity));
    }

    [Fact]
    public void The_same_product_twice_is_refused()
    {
        var lines = new[] { new NewVanReturnLine(_soap, null, 1), new NewVanReturnLine(_soap, null, 2) };

        Assert.Throws<VanReturnRuleException>(() => VanReturn.ValidateDeclaredLines(lines));
    }
}
