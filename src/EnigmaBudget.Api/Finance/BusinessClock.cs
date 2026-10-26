using System.Globalization;

namespace EnigmaBudget.Api.Finance;

/// <summary>Provides the current instant and financial calendar date for Argentina.</summary>
public sealed class BusinessClock(TimeProvider timeProvider)
{
    private static readonly TimeZoneInfo ArgentinaTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("America/Argentina/Buenos_Aires");

    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();

    public DateOnly Today => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(UtcNow, ArgentinaTimeZone).DateTime
    );

    public string CurrentPeriod => Today.ToString("yyyy-MM", CultureInfo.InvariantCulture);
}
