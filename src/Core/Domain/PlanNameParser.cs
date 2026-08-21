namespace Core.Domain;

public static partial class PlanNameParser
{
    // "[GFR][2026][Delivery Plan] - September 14th QED Release" -> 2026-09-14
    private static readonly Regex Pattern = MyDeliveryRegex();

    public static LocalDate? ParseGoalDate(string name)
    {
        var m = Pattern.Match(name);
        if (!m.Success) return null;

        var year = int.Parse(m.Groups["year"].Value);
        var day = int.Parse(m.Groups["day"].Value);
        if (!TryMonth(m.Groups["month"].Value, out var month)) return null;

        try { return new LocalDate(year, month, day); }
        catch { return null; }   // guard against impossible dates
    }

    private static bool TryMonth(string name, out int month)
    {
        month = Array.FindIndex(
            System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.MonthNames,
            n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) + 1;
        return month >= 1 && month <= 12;
    }

    [GeneratedRegex(@"\[GFR\]\[(?<year>\d{4})\]\[Delivery Plan\]\s*-\s*(?<month>[A-Za-z]+)\s+(?<day>\d{1,2})(?:st|nd|rd|th)", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex MyDeliveryRegex();
}