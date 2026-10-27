namespace Sellora.OrderService.Domain.Orders;

/// <summary>
/// Plain-language durations for messages a shop owner reads: being 6
/// minutes late is a different conversation from being 6 hours late.
/// </summary>
public static class DurationText
{
    public static string Describe(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = duration.Negate();
        }

        if (duration < TimeSpan.FromMinutes(1))
        {
            var seconds = Math.Max(1, (int)Math.Floor(duration.TotalSeconds));
            return Unit(seconds, "second");
        }

        var days = duration.Days;
        var hours = duration.Hours;
        var minutes = duration.Minutes;

        if (days > 0)
        {
            return hours > 0 ? $"{Unit(days, "day")} {Unit(hours, "hour")}" : Unit(days, "day");
        }

        if (hours > 0)
        {
            return minutes > 0 ? $"{Unit(hours, "hour")} {Unit(minutes, "minute")}" : Unit(hours, "hour");
        }

        return Unit(minutes, "minute");
    }

    private static string Unit(int value, string unit) => value == 1 ? $"1 {unit}" : $"{value} {unit}s";
}
