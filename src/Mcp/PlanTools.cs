namespace Mcp;

public record MonthAnchor(
    [property: Description("Month 1-12")] int Month,
    [property: Description("Anchor date ISO yyyy-MM-dd (Release for Prod months, QED for busy season)")] string Anchor);

public record HolidayInput(
    [property: Description("Holiday date ISO yyyy-MM-dd")] string Date,
    [property: Description("Display name, e.g. 'New Year's Day'")] string Name);

[McpServerToolType]
public static class PlanTools
{
    private static readonly int[] BusySeasonMonths = [1, 2, 3, 9];
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


    [McpServerTool, Description(
    "Creates GFR delivery plans in Azure DevOps from single-anchor months. Uses the same " +
    "engine as compute_plan_year, then WRITES each plan to ADO. Idempotent: a plan whose " +
    "name already exists is skipped. Returns created/skipped/errors per month.")]
    public static async Task<object> CreatePlanYear(
    [Description("Year, e.g. 2026")] int year,
    [Description("One or more months with their anchor date")] MonthAnchor[] anchors,
    IHolidayReader holidays,
    IDeliveryPlanCatalog catalog,
    IDeliveryPlanWriter writer,
    CancellationToken ct = default)
    {
        if (anchors is null || anchors.Length == 0)
            throw new ArgumentException("Provide at least one month anchor.");

        var holidayDates = await holidays.GetHolidaysAsync(year, ct);
        var calendar = holidayDates is not null
            ? new HolidayCalendar(holidayDates)
            : CompanyHolidays.Calendar(year, year + 1);

        var existing = await catalog.FindPlansAsync("[GFR]", ct);
        var existingNames = existing.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var created = new List<object>();
        var skipped = new List<object>();
        var errors = new List<object>();

        foreach (var a in anchors)
        {
            try
            {
                var schedule = BuildSchedule(year, a);
                var plan = DeliveryPlanJob.BuildPlan(schedule, calendar);

                if (existingNames.Contains(schedule.PlanName))
                {
                    skipped.Add(new { month = a.Month, planName = schedule.PlanName, reason = "already exists" });
                    continue;
                }

                var tags = TagsFor(year, a.Month);
                var options = new PlanPublishOptions(schedule.PlanName, tags);

                var refCreated = await writer.CreateAsync(plan, options, ct);
                created.Add(new { month = a.Month, planId = refCreated.Id, planName = refCreated.Name, tags });
            }
            catch (Exception ex)
            {
                errors.Add(new { month = a.Month, reason = ex.Message });
            }
        }

        return new
        {
            year,
            createdCount = created.Count,
            skippedCount = skipped.Count,
            errorCount = errors.Count,
            created,
            skipped,
            errors
        };
    }

    [McpServerTool, Description(
        "Loads/updates the official company holidays for a given year into the store used " +
        "by the delivery-plan engine. Overwrites that year's holiday file. Each holiday has " +
        "an ISO date (yyyy-MM-dd) and a display name."
        )]
    public static async Task<object> LoadHolidayForYear(
        [Description("Year, e.g. 2026")] int year,
        [Description("The holidays to load for that year")] HolidayInput[] holidays,
        IHolidayWriter writer,
        CancellationToken ct = default)
    {
        if (holidays is null || holidays.Length == 0)
            throw new ArgumentException("Provide at least one holiday.");

        var parsed = holidays
            .Select(h => new Holiday(ParseDate(h.Date, nameof(h.Date)), h.Name))
        .ToList();

        await writer.WriteAsync(year, parsed, ct);

        return new
        {
            year,
            count = parsed.Count,
            holidays = parsed.Select(h => new { date = Iso(h.Date), name = h.Name })
        };
    }

    private static IReadOnlyList<string> TagsFor(int year, int month)
    {
        if (!IsBusySeason(month))
            return [$"{year}.{month:D2}"];
        var next = NextProdMonth(month);
        return [$"{year}.{month:D2}_QED", $"{year}.{next:D2}"];
    }

    private static int NextProdMonth(int month)
    {
        var m = month + 1;
        while (m <= 12 && IsBusySeason(m)) m++;
        return m;
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