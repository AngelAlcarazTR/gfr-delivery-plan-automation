namespace Mcp;

/// <summary>
/// Represents a month and its associated anchor date for GFR delivery planning.
/// </summary>
/// <param name="Month">The month (1-12) for which the anchor date is specified.</param>
/// <param name="Anchor">The anchor date in ISO yyyy-MM-dd format (Release for Prod months, QED for busy season).</param>
public record MonthAnchor(
    [property: Description("Month 1-12")] int Month,
    [property: Description("Anchor date ISO yyyy-MM-dd (Release for Prod months, QED for busy season)")] string Anchor,
    [property: Description("Optional manual overrides applied AFTER the deterministic engine, e.g. to nudge a milestone off a holiday")] MarkerOverride[]? Overrides = null);

/// <summary>
/// Represents a manual override that moves a single plan milestone to a specific date,
/// applied after the deterministic engine. Used to nudge a milestone off a holiday.
/// </summary>
/// <param name="Marker">The milestone to move: StartDev, EndDev, QaCutoff, QedDeploy, StartReg, EndReg, Release.</param>
/// <param name="Date">The new date in ISO yyyy-MM-dd format.</param>
public record MarkerOverride(
    [property: Description("Marker to move: StartDev, EndDev, QaCutoff, QedDeploy, StartReg, EndReg, Release")] string Marker,
    [property: Description("New date ISO yyyy-MM-dd")] string Date);

/// <summary>
/// Represents a holiday input with a date and display name for GFR delivery planning.
/// </summary>
/// <param name="Date">The holiday date in ISO yyyy-MM-dd format.</param>
/// <param name="Name">The display name of the holiday, e.g. 'New Year's Day'.</param>
public record HolidayInput(
    [property: Description("Holiday date ISO yyyy-MM-dd")] string Date,
    [property: Description("Display name, e.g. 'New Year's Day'")] string Name);

/// <summary>
/// Provides tools for computing and creating GFR delivery plans, as well as managing company holidays.
/// </summary>
[McpServerToolType]
public static class PlanTools
{
    private static readonly int[] BusySeasonMonths = [1, 2, 3, 9];
    private static bool IsBusySeason(int month) => Array.IndexOf(BusySeasonMonths, month) >= 0;
    private static IEnumerable<int> MarkerYears(DeliveryPlan plan)
       => plan.Events.Select(e => e.Date.Year).Distinct();

    private static async Task<IReadOnlyList<HolidayConflict>> WarningsFor(
        DeliveryPlan plan, IHolidayCalendarSource source, CancellationToken ct)
    {
        var calendars = await source.GetCalendarAsync(MarkerYears(plan), ct);
        return HolidayConflictDetector.Detect(plan.Events, calendars);
    }

    /// <summary>
    /// Computes GFR delivery plans for one or more months of a year, each from a single calendar anchor. 
    /// The plan kind is inferred from the month: busy-season months (Jan, Feb, Mar, Sep) are QED-only and the anchor is the QED deploy date; 
    /// all other months are Prod and the anchor is the production Release date. Returns each plan's name and milestones, plus a per-month error list for any anchors that failed.
    /// </summary>
    /// <param name="year">The year for which to compute the delivery plans.</param>
    /// <param name="anchors">An array of MonthAnchor objects representing the months and their anchor dates.</param>
    /// <param name="holidaySource">An IHolidayCalendarSource instance to retrieve holiday dates.</param>
    /// <param name="ct">A CancellationToken to observe while waiting for the task to complete.</param>
    /// <returns>An object containing the year, count of successful plans, the plans themselves, and any errors encountered.</returns>
    /// <exception cref="ArgumentException">Thrown when no month anchors are provided.</exception>
    [McpServerTool, Description(
        "Computes GFR delivery plans for one or more months of a year, each from a single " +
        "calendar anchor. The plan kind is inferred from the month: busy-season months " +
        "(Jan, Feb, Mar, Sep) are QED-only and the anchor is the QED deploy date; all other " +
        "months are Prod and the anchor is the production Release date. Each anchor accepts " +
        "optional 'overrides' to manually move specific markers off a holiday; overridden markers " +
        "come back with adjusted=true and originalDate. Preview overrides here before create_plan_year " +
        "writes them to ADO. Returns each plan's " +
        "name and milestones, plus a per-month error list for any anchors that failed. Each plan includes " +
        "'warningsCount' and 'warningsSummary'; whenever warningsCount > 0 you MUST report these holiday " +
        "conflicts to the user. They are informational only — the schedule is never shifted. " +
        "Plans also include 'orderWarnings' when a manual override leaves milestones out of " +
        "chronological order (e.g. QedDeploy before EndDev); report them too, but they do not " +
        "block creation.")]
    public static async Task<object> ComputePlanYear(
        [Description("Year, e.g. 2026")] int year,
        [Description("One or more months with their anchor date")] MonthAnchor[] anchors,
        IHolidayCalendarSource holidaySource,
        CancellationToken ct = default)
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
                plan = ApplyOverrides(plan, a.Overrides);

                var markers = plan.Events
                    .Select(e => new
                    {
                        label = e.Label.ToString(),
                        date = Iso(e.Date),
                        adjusted = e.Adjusted,
                        originalDate = e.OriginalDate is { } od ? Iso(od) : null
                    })
                    .ToList();

                var conflicts = await WarningsFor(plan, holidaySource, ct);

                var warnings = conflicts.Select(c => new
                {
                    marker = c.Marker.ToString(),
                    date = Iso(c.Date),
                    country = c.Country,
                    region = c.Region,
                    holiday = c.HolidayName
                }).ToList();

                var warningsSummary = conflicts
                    .Select(c => $"{c.Marker} {Iso(c.Date)} falls on a public holiday in {c.Country}-{c.Region}: {c.HolidayName}")
                    .ToList();

                var orderWarnings = OrderWarnings(plan);

                plans.Add(new
                {
                    month = a.Month,
                    kind = schedule.Kind.ToString(),
                    planName = schedule.PlanName,
                    markers,
                    warningsCount = warnings.Count,
                    warnings,
                    warningsSummary,
                    orderWarnings
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

    /// <summary>
    /// Creates GFR delivery plans in Azure DevOps from single-anchor months. Uses the same engine as compute_plan_year, then WRITES each plan to ADO. 
    /// Idempotent: a plan whose name already exists is skipped. Returns created/skipped/errors per month.
    /// </summary>
    /// <param name="year">The year for which to create the delivery plans.</param>
    /// <param name="anchors">An array of MonthAnchor objects representing the months and their anchor dates.</param>
    /// <param name="holidays">An IHolidayReader instance to retrieve holiday dates.</param>
    /// <param name="catalog">An IDeliveryPlanCatalog instance to check for existing plans.</param>
    /// <param name="writer">An IDeliveryPlanWriter instance to write the delivery plans to Azure DevOps.</param>
    /// <param name="holidaySource">An IHolidayCalendarSource instance to retrieve holiday calendar information.</param>
    /// <param name="ct">A token to monitor for cancellation requests.</param>
    /// <returns>An object containing the year, counts of created, skipped, and errored plans, and the details of each.</returns>
    /// <exception cref="ArgumentException">Thrown if no month anchors are provided.</exception>
    [McpServerTool, Description(
    "Creates GFR delivery plans in Azure DevOps from single-anchor months. Uses the same " +
    "engine as compute_plan_year, then WRITES each plan to ADO. Each anchor accepts optional " +
    "'overrides' to manually move specific markers before writing (preview them first with " +
    "compute_plan_year). Idempotent: a plan whose name already exists is skipped. Returns " +
    "created/skipped/errors per month. Each created plan includes 'warnings' (holiday conflicts) " +
    "and 'orderWarnings' (milestones out of chronological order after an override); both are advisory.")]
    public static async Task<object> CreatePlanYear(
    [Description("Year, e.g. 2026")] int year,
    [Description("One or more months with their anchor date")] MonthAnchor[] anchors,
    IHolidayReader holidays,
    IDeliveryPlanCatalog catalog,
    IDeliveryPlanWriter writer,
    IHolidayCalendarSource holidaySource,
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
                plan = ApplyOverrides(plan, a.Overrides);
                var warnings = await WarningsFor(plan, holidaySource, ct);
                var orderWarnings = OrderWarnings(plan);

                if (existingNames.Contains(schedule.PlanName))
                {
                    skipped.Add(new { month = a.Month, planName = schedule.PlanName, reason = "already exists" });
                    continue;
                }

                var tags = TagsFor(year, a.Month);
                var options = new PlanPublishOptions(schedule.PlanName, tags);

                var refCreated = await writer.CreateAsync(plan, options, ct);
                created.Add(new { month = a.Month, planId = refCreated.Id, planName = refCreated.Name, tags, warnings, orderWarnings });
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

    /// <summary>
    /// Reads the CURRENT GFR delivery plan straight from Azure DevOps and returns it as structured data
    /// (plan name, id, owner, goal date, tags and the milestone markers). The "current" plan is chosen by
    /// goal date (nearest goal on/after the reference date, falling back to the most recent past plan).
    /// Reads the plan AS IT STANDS in ADO — including manual edits — it does NOT recompute it.
    /// </summary>
    /// <param name="catalog">An IDeliveryPlanCatalog instance to list/filter candidate plans.</param>
    /// <param name="reader">An IDeliveryPlanReader instance to read the selected plan's markers.</param>
    /// <param name="holidaySource">An IHolidayCalendarSource instance to detect holiday conflicts.</param>
    /// <param name="filter">Free-text filter matching the plan name or owner. Defaults to "[GFR]".</param>
    /// <param name="asOf">Optional reference date ISO yyyy-MM-dd. Defaults to today.</param>
    /// <param name="ct">A CancellationToken to observe while waiting for the task to complete.</param>
    /// <returns>An object describing the current plan, or found=false when no plan matches the filter.</returns>
    [McpServerTool, Description(
        "Reads the CURRENT GFR delivery plan straight from Azure DevOps and returns it as " +
        "structured data (plan name, id, owner, goal date, tags and the milestone markers). " +
        "The 'current' plan is chosen by goal date: the plan whose goal is the nearest date on " +
        "or after 'asOf' (today by default), falling back to the most recent past plan. This " +
        "reads the REAL plan as it stands in ADO — including any manual edits — it does NOT " +
        "recompute it. Includes 'warnings' (markers landing on a public holiday) and " +
        "'orderWarnings' (markers out of chronological order); both are advisory. Returns " +
        "found=false when no plan matches the filter.")]
    public static async Task<object> GetCurrentPlan(
        IDeliveryPlanCatalog catalog,
        IDeliveryPlanReader reader,
        IHolidayCalendarSource holidaySource,
        [Description("Free-text filter matching the plan name or owner, e.g. '[GFR]'. Defaults to '[GFR]'.")] string filter = "[GFR]",
        [Description("Reference date ISO yyyy-MM-dd. The current plan is the nearest goal on/after this date. Defaults to today.")] string? asOf = null,
        CancellationToken ct = default)
    {
        var today = string.IsNullOrWhiteSpace(asOf)
            ? LocalDate.FromDateTime(DateTime.Today)
            : ParseDate(asOf, nameof(asOf));

        var matches = await catalog.FindPlansAsync(filter, ct);
        var current = CurrentPlanSelector.Pick(matches, today);

        if (current is null)
            return new
            {
                found = false,
                filter,
                asOf = Iso(today),
                candidateCount = matches.Count,
                reason = "No plan with a parseable goal date matched the filter."
            };

        var plan = await reader.GetPlanAsync(current.Id, ct);

        var markers = plan.Events
            .OrderBy(e => e.Date)
            .Select(e => new
            {
                label = e.Label.ToString(),
                date = Iso(e.Date),
                adjusted = e.Adjusted,
                originalDate = e.OriginalDate is { } od ? Iso(od) : null
            })
            .ToList();

        var conflicts = await WarningsFor(plan, holidaySource, ct);

        var warnings = conflicts.Select(c => new
        {
            marker = c.Marker.ToString(),
            date = Iso(c.Date),
            country = c.Country,
            region = c.Region,
            holiday = c.HolidayName
        }).ToList();

        var warningsSummary = conflicts
            .Select(c => $"{c.Marker} {Iso(c.Date)} falls on a public holiday in {c.Country}-{c.Region}: {c.HolidayName}")
            .ToList();

        var orderWarnings = OrderWarnings(plan);

        return new
        {
            found = true,
            planId = plan.PlanId ?? current.Id,
            planName = current.Name,
            owner = current.Owner,
            goalDate = current.GoalDate is { } g ? Iso(g) : null,
            asOf = Iso(today),
            tags = plan.Tags,
            markers,
            warningsCount = warnings.Count,
            warnings,
            warningsSummary,
            orderWarnings
        };
    }

    /// <summary>
    /// Moves a single milestone marker of an EXISTING GFR delivery plan in Azure DevOps to a new date,
    /// in place, leaving every other marker and setting untouched. Then re-reads the plan and returns
    /// its markers plus fresh holiday / chronological-order warnings. Typically used to nudge a marker
    /// off a public holiday flagged by get_current_plan / compute_plan_year.
    /// </summary>
    /// <param name="writer">An IDeliveryPlanWriter instance to persist the marker change to ADO.</param>
    /// <param name="reader">An IDeliveryPlanReader instance to re-read the plan after the update.</param>
    /// <param name="holidaySource">An IHolidayCalendarSource instance to detect holiday conflicts.</param>
    /// <param name="planId">The ADO delivery plan id (GUID) to update, e.g. from get_current_plan.</param>
    /// <param name="marker">The milestone to move: StartDev, EndDev, QaCutoff, QedDeploy, StartReg, EndReg, Release.</param>
    /// <param name="date">The new date in ISO yyyy-MM-dd format.</param>
    /// <param name="ct">A CancellationToken to observe while waiting for the task to complete.</param>
    /// <returns>An object describing the update result and the re-read plan, or updated=false when the marker is not found.</returns>
    /// <exception cref="ArgumentException">Thrown when planId is empty or the marker name is unknown.</exception>
    [McpServerTool, Description(
        "Moves a SINGLE milestone marker of an EXISTING GFR delivery plan in Azure DevOps to a " +
        "new date, in place — every other marker and setting is preserved. Use it to nudge a " +
        "marker off a public holiday flagged by get_current_plan or compute_plan_year. Pass the " +
        "plan id (planId) from get_current_plan, the marker name (StartDev, EndDev, QaCutoff, " +
        "QedDeploy, StartReg, EndReg, Release) and the new ISO date. After writing, it re-reads " +
        "the plan and returns its markers plus fresh 'warnings' (holiday conflicts) and " +
        "'orderWarnings' (markers now out of chronological order) so you can confirm the result. " +
        "Returns updated=false when the marker is not present on the plan. This WRITES to ADO — " +
        "preview the target date with the user first.")]
    public static async Task<object> UpdatePlanMarker(
        IDeliveryPlanWriter writer,
        IDeliveryPlanReader reader,
        IHolidayCalendarSource holidaySource,
        [Description("ADO delivery plan id (GUID) to update, e.g. from get_current_plan")] string planId,
        [Description("Marker to move: StartDev, EndDev, QaCutoff, QedDeploy, StartReg, EndReg, Release")] string marker,
        [Description("New date ISO yyyy-MM-dd")] string date,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(planId))
            throw new ArgumentException("Provide the plan id (planId).");

        if (!Enum.TryParse<Milestone>(marker, ignoreCase: true, out var milestone))
            throw new ArgumentException(
                $"Unknown marker '{marker}'. Valid: StartDev, EndDev, QaCutoff, QedDeploy, StartReg, EndReg, Release.");

        var newDate = ParseDate(date, nameof(date));

        var result = await writer.UpdateMarkerDateAsync(planId, milestone, newDate, ct);

        if (!result.Found)
            return new
            {
                updated = false,
                planId,
                marker = milestone.ToString(),
                reason = "Marker not found on this plan."
            };

        // Re-read the persisted plan so the caller sees the real state + fresh warnings.
        var plan = await reader.GetPlanAsync(planId, ct);

        var markers = plan.Events
            .OrderBy(e => e.Date)
            .Select(e => new
            {
                label = e.Label.ToString(),
                date = Iso(e.Date),
                adjusted = e.Adjusted,
                originalDate = e.OriginalDate is { } od ? Iso(od) : null
            })
            .ToList();

        var conflicts = await WarningsFor(plan, holidaySource, ct);

        var warnings = conflicts.Select(c => new
        {
            marker = c.Marker.ToString(),
            date = Iso(c.Date),
            country = c.Country,
            region = c.Region,
            holiday = c.HolidayName
        }).ToList();

        var warningsSummary = conflicts
            .Select(c => $"{c.Marker} {Iso(c.Date)} falls on a public holiday in {c.Country}-{c.Region}: {c.HolidayName}")
            .ToList();

        var orderWarnings = OrderWarnings(plan);

        return new
        {
            updated = true,
            planId,
            planName = plan.Sprint.SprintId,
            marker = milestone.ToString(),
            previousDate = result.PreviousDate is { } p ? Iso(p) : null,
            newDate = Iso(newDate),
            markers,
            warningsCount = warnings.Count,
            warnings,
            warningsSummary,
            orderWarnings
        };
    }

    /// <summary>
    /// Loads/updates the official company holidays for a given year into the store used by the delivery-plan engine. Overwrites that year's holiday file. Each holiday has an ISO date (yyyy-MM-dd) and a display name.
    /// </summary>
    /// <param name="year">The year for which to load holidays, e.g. 2026</param>
    /// <param name="holidays">The holidays to load for that year</param>
    /// <param name="writer">The holiday writer to use</param>
    /// <param name="country">Optional ISO country code, e.g. 'MX','US','IN'. Omit for the global file.</param>
    /// <param name="region">Optional region/state code, e.g. 'KA','TG' for India.</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The result of the holiday load operation</returns>
    /// <exception cref="ArgumentException">Thrown when no holidays are provided</exception>
    [McpServerTool, Description(
        "Loads/updates the official company holidays for a given year into the store used " +
        "by the delivery-plan engine. Overwrites that year's holiday file. Each holiday has " +
        "an ISO date (yyyy-MM-dd) and a display name."
        )]
    public static async Task<object> LoadHolidayForYear(
        [Description("Year, e.g. 2026")] int year,
        [Description("The holidays to load for that year")] HolidayInput[] holidays,
        IHolidayWriter writer,
        [Description("Optional ISO country code, e.g. 'MX','US','IN'. Omit for the global file.")] string? country = null,
        [Description("Optional region/state code, e.g. 'KA','TG' for India.")] string? region = null,
        CancellationToken ct = default)
    {
        if (holidays is null || holidays.Length == 0)
            throw new ArgumentException("Provide at least one holiday.");

        var parsed = holidays
            .Select(h => new Holiday(ParseDate(h.Date, nameof(h.Date)), h.Name))
            .ToList();

        await writer.WriteAsync(year, country, region!, parsed, ct);

        return new
        {
            year,
            country,
            region,
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

    internal static DeliveryPlan ApplyOverrides(DeliveryPlan plan, MarkerOverride[]? overrides)
    {
        if (overrides is null || overrides.Length == 0)
            return plan;

        var byLabel = plan.Events.ToDictionary(e => e.Label);
        foreach (var o in overrides)
        {
            if (!Enum.TryParse<Milestone>(o.Marker, ignoreCase: true, out var label) || !byLabel.ContainsKey(label))
                throw new ArgumentException($"Unknown marker '{o.Marker}' for this plan.");

            var current = byLabel[label];
            byLabel[label] = current with
            {
                Date = ParseDate(o.Date, nameof(o.Date)),
                Adjusted = true,
                OriginalDate = current.OriginalDate ?? current.Date
            };
        }

        var newEvents = plan.Events.Select(e => byLabel[e.Label]).ToList();
        return plan with { Events = newEvents };
    }

    internal static List<string> OrderWarnings(DeliveryPlan plan)
    {
        var ordered = plan.Events.OrderBy(e => (int)e.Label).ToList();
        var result = new List<string>();
        for (var i = 1; i < ordered.Count; i++)
        {
            var prev = ordered[i - 1];
            var curr = ordered[i];
            if (prev.Date.CompareTo(curr.Date) > 0)
                result.Add($"{curr.Label} {Iso(curr.Date)} is before {prev.Label} {Iso(prev.Date)}");
        }
        return result;
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