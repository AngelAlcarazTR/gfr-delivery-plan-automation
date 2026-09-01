namespace Mcp;

public record MonthAnchor(
    [property: Description("Month 1-12")] int Month,
    [property: Description("Anchor date ISO yyyy-MM-dd (Release for Prod months, QED for busy season)")] string Anchor);

[McpServerToolType]
public static class PlanTools
{
    private static readonly int[] BusySeasonMonths = { 1, 2, 3, 9 };
    private static bool IsBusySeason(int month) => Array.IndexOf(BusySeasonMonths, month) >= 0;

    [McpServerTool, Description(
        "Computes GFR delivery plans for one or more months of a year, each from a single " +
        "calendar anchor. The plan kind is inferred from the month: busy-season months " +
        "(Jan, Feb, Mar, Sep) are QED-only and the anchor is the QED deploy date; all other " +
        "months are Prod and the anchor is the production Release date. Returns each plan's " +
        "name and milestones, plus a per-month error list for any anchors that failed.")]
    public static object ComputePlanYear(
        [Description("Year, e.g. 2026")] int year,
        [Description("One or more months with their anchor date")] MonthAnchor[] anchors)
    {
        if (anchors is null || anchors.Length == 0)
            throw new ArgumentException("Provide at least one month anchor.");

        var plans = new List<object>();
        var errors = new List<object>();

        foreach (var a in anchors)
        {
            try
            {
                var schedule = BuildSchedule(year, a);
                var plan = DeliveryPlanJob.BuildPlan(schedule);

                var markers = plan.Events
                    .Select(e => new { label = e.Label.ToString(), date = Iso(e.Date) })
                    .ToList();

                plans.Add(new
                {
                    month = a.Month,
                    kind = schedule.Kind.ToString(),
                    planName = schedule.PlanName,
                    markers
                });
            }
            catch (Exception ex)
            {
                errors.Add(new { month = a.Month, reason = ex.Message });
            }
        }

        return new
        {
            year,
            count = plans.Count,
            plans,
            errors
        };
    }

    private static ReleaseSchedule BuildSchedule(int year, MonthAnchor a)
    {
        if (a.Month is < 1 or > 12)
            throw new ArgumentException($"Invalid month '{a.Month}'.");

        var kind = IsBusySeason(a.Month) ? PlanKind.QedOnly : PlanKind.Prod;
        var anchor = ParseDate(a.Anchor, nameof(a.Anchor));
        var planName = BuildPlanName(year, kind, anchor);
        return new ReleaseSchedule(kind, anchor, planName);
    }

    private static string BuildPlanName(int year, PlanKind kind, LocalDate goal)
    {
        var month = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(goal.Month);
        var day = goal.Day;
        var kindText = kind == PlanKind.Prod ? "Release" : "QED Release";
        return $"[GFR][{year}][Delivery Plan] - {month} {day}{Ordinal(day)} {kindText}";
    }

    private static string Ordinal(int day) =>
        (day % 100) switch
        {
            11 or 12 or 13 => "th",
            _ => (day % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" }
        };

    private static LocalDate ParseDate(string iso, string field)
    {
        if (DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return new LocalDate(dt.Year, dt.Month, dt.Day);
        throw new ArgumentException($"Invalid date in '{field}': '{iso}' (expected yyyy-MM-dd).");
    }

    private static string Iso(LocalDate d) => $"{d.Year:D4}-{d.Month:D2}-{d.Day:D2}";
}