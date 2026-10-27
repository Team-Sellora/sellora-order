using Sellora.OrderService.Domain.Orders;

namespace Sellora.OrderService.Tests;

public sealed class GeoDistanceTests
{
    [Fact]
    public void Same_point_is_zero()
    {
        Assert.Equal(0, GeoDistance.Meters(GeoTestPoints.Shop, GeoTestPoints.Shop));
    }

    [Fact]
    public void One_thousandth_of_a_degree_at_the_equator_is_about_111_metres()
    {
        var distance = GeoDistance.Meters(new GeoPoint(0, 0), new GeoPoint(0, 0.001));

        Assert.Equal(111.20, distance, precision: 2);
    }

    [Fact]
    public void Distance_is_symmetric()
    {
        var other = new GeoPoint(6.9271, 79.8612);

        Assert.Equal(
            GeoDistance.Meters(GeoTestPoints.Shop, other),
            GeoDistance.Meters(other, GeoTestPoints.Shop));
    }

    [Theory]
    [InlineData(120)]
    [InlineData(299)]
    [InlineData(300)]
    [InlineData(301)]
    [InlineData(450)]
    public void Constructed_points_measure_exactly_as_intended(double meters)
    {
        var point = GeoTestPoints.NorthOf(GeoTestPoints.Shop, meters);

        Assert.Equal(meters, GeoDistance.Meters(GeoTestPoints.Shop, point));
    }

    [Theory]
    [InlineData(91, 0)]
    [InlineData(-91, 0)]
    [InlineData(0, 181)]
    [InlineData(0, -181)]
    [InlineData(double.NaN, 0)]
    public void Out_of_range_coordinates_are_refused(double latitude, double longitude)
    {
        var exception = Assert.Throws<CheckoutRuleViolationException>(() => new GeoPoint(latitude, longitude));

        Assert.Equal(CheckoutFailure.InvalidCoordinates, exception.Failure);
    }
}
