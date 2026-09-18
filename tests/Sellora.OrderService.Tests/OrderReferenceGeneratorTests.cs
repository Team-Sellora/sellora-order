using System.Text.RegularExpressions;
using Sellora.OrderService.Domain.Orders;

namespace Sellora.OrderService.Tests;

public sealed class OrderReferenceGeneratorTests
{
    [Fact]
    public void Reference_is_short_dated_and_free_of_ambiguous_characters()
    {
        var date = new DateTimeOffset(2026, 9, 18, 10, 0, 0, TimeSpan.Zero);

        for (var i = 0; i < 500; i++)
        {
            var reference = OrderReferenceGenerator.Generate(date);

            Assert.Matches(new Regex("^ORD-260918-[23456789ABCDEFGHJKMNPQRSTWXYZ]{6}$"), reference);
            Assert.DoesNotContain(reference[11..], c => "01ILOUV".Contains(c));
        }
    }

    [Fact]
    public void Random_part_is_well_spread()
    {
        // Duplicates are possible by design (birthday problem); the unique
        // index plus retry in OrderCreationService is the real guarantee.
        // Here we only prove the generator is random, not stuck or biased.
        var references = Enumerable.Range(0, 1_000)
            .Select(_ => OrderReferenceGenerator.Generate(DateTimeOffset.UtcNow))
            .ToList();

        Assert.True(references.Distinct().Count() >= 995);
    }
}
