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
    /// Lists the GFR delivery plans known to Azure DevOps as a lightweight inventory (id, name,
    /// owner, goal date and last-modified), sorted by goal date. Optionally narrows to a single
    /// year and flags which plan is the 'current' one (the nearest goal on/after asOf). Use it to
    /// audit the year at a glance or to discover a plan id to feed the other tools; it does NOT
    /// read markers — use get_current_plan for a single plan's detail.
    /// </summary>
    /// <param name="catalog">An IDeliveryPlanCatalog instance to enumerate the plans.</param>
    /// <param name="filter">Free-text filter matching the plan name or owner. Defaults to "[GFR]".</param>
    /// <param name="catalog">An IDeliveryPlanCatalog instance to enumerate the plans.</param>
    /// <param name="ado">The AdoConfig used to build each plan's Delivery Plan deep-link.</param>
    /// <param name="filter">Free-text filter matching the plan name or owner. Defaults to "[GFR]".</param>
    /// <param name="year">Optional year to narrow the list to plans whose goal date is in that year.</param>
    /// <param name="asOf">Optional reference date ISO yyyy-MM-dd used to flag the current plan. Defaults to today.</param>
    /// <param name="ct">A CancellationToken to observe while waiting for the task to complete.</param>
    /// <returns>An object with the plan inventory sorted by goal date, and the current plan id.</returns>
    [McpServerTool, Description(
        "Lists the GFR delivery plans in Azure DevOps as a lightweight inventory — id, name, " +
        "owner, goal date and last-modified — sorted by goal date, with undated plans last. " +
        "Optionally pass 'year' to narrow to a single year's plans, and 'asOf' (today by default) " +
        "to flag which plan is 'current' (isCurrent=true is the nearest goal on/after asOf; its " +
        "id is also echoed as currentPlanId). Each plan carries a ready-made markdown link in " +
        "'view' (e.g. '[View](https://dev.azure.com/.../plan/{id})') plus the raw 'url'. When you " +
        "present this list to the user, render the plan name followed by the SHORT 'view' link " +
        "(the word 'View' as a clickable link) — do NOT paste the long raw url. Use this to audit " +
        "the whole year or to find a plan id to feed get_current_plan, update_plan_marker or " +
        "auto_resolve_holidays. It does NOT return markers or warnings — call get_current_plan " +
        "for a single plan's detail.")]
    public static async Task<object> ListPlans(
        IDeliveryPlanCatalog catalog,
        AdoConfig ado,
        [Description("Free-text filter matching the plan name or owner, e.g. '[GFR]'. Defaults to '[GFR]'.")] string filter = "[GFR]",
        [Description("Optional year to narrow to plans whose goal date is in that year, e.g. 2026")] int? year = null,
        [Description("Reference date ISO yyyy-MM-dd used to flag the current plan. Defaults to today.")] string? asOf = null,
        CancellationToken ct = default)
    {
        var today = string.IsNullOrWhiteSpace(asOf)
            ? LocalDate.FromDateTime(DateTime.Today)
            : ParseDate(asOf, nameof(asOf));

        var matches = await catalog.FindPlansAsync(filter, ct);

        var scoped = year is { } y
            ? matches.Where(p => p.GoalDate?.Year == y).ToList()
            : matches.ToList();

        var current = CurrentPlanSelector.Pick(scoped, today);

        // Dated plans ascending by goal date; undated ones (unparseable name) last.
        var ordered = scoped
            .OrderBy(p => p.GoalDate is null)
            .ThenBy(p => p.GoalDate ?? LocalDate.MaxIsoValue)
            .Select(p =>
            {
                var url = AdoPlanLinks.DeliveryPlanUrl(ado, p.Id);
                return new
                {
                    id = p.Id,
                    name = p.Name,
                    owner = p.Owner,
                    goalDate = p.GoalDate is { } g ? Iso(g) : null,
                    modifiedAt = InstantPattern.ExtendedIso.Format(p.ModifiedAt),
                    isCurrent = current is not null && p.Id == current.Id,
                    url,
                    view = $"[View]({url})"
                };
            })
            .ToList();

        return new
        {
            filter,
            year,
            asOf = Iso(today),
            count = ordered.Count,
            currentPlanId = current?.Id,
            plans = ordered
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
    /// Scans an EXISTING GFR delivery plan in Azure DevOps for markers landing on a public
    /// holiday (or weekend) and rolls each conflicting marker FORWARD to the nearest business
    /// day that is clear in every holiday calendar. Preview-first: with dryRun=true (default)
    /// it only proposes the moves; with dryRun=false it applies them via UpdateMarkerDateAsync,
    /// re-reads the plan and reports any remaining warnings.
    /// </summary>
    /// <param name="reader">An IDeliveryPlanReader instance to read (and re-read) the plan.</param>
    /// <param name="writer">An IDeliveryPlanWriter instance to persist the moves when applying.</param>
    /// <param name="holidaySource">An IHolidayCalendarSource instance to detect holiday conflicts.</param>
    /// <param name="planId">The ADO delivery plan id (GUID) to resolve, e.g. from get_current_plan.</param>
    /// <param name="dryRun">When true (default) only proposes moves; when false applies them to ADO.</param>
    /// <param name="ct">A CancellationToken to observe while waiting for the task to complete.</param>
    /// <returns>An object describing the proposed or applied resolutions and, when applied, the re-read plan.</returns>
    /// <exception cref="ArgumentException">Thrown when planId is empty.</exception>
    [McpServerTool, Description(
        "Auto-resolves holiday conflicts on an EXISTING GFR delivery plan in Azure DevOps: it " +
        "finds every marker that lands on a public holiday (or weekend) and rolls it FORWARD to " +
        "the nearest business day that is clear in ALL holiday calendars. Preview-first — with " +
        "dryRun=true (the default) it ONLY proposes the moves and writes nothing; re-run with " +
        "dryRun=false to actually apply them. Pass the plan id (planId) from get_current_plan. " +
        "When applied it moves each marker via the same in-place update as update_plan_marker, " +
        "then re-reads the plan and returns its markers plus any 'remainingWarnings' (should be " +
        "empty) and 'orderWarnings' (moving a marker forward can push it out of chronological " +
        "order — advisory, never blocking). Returns conflictCount=0 when the plan is already clean.")]
    public static async Task<object> AutoResolveHolidays(
        IDeliveryPlanReader reader,
        IDeliveryPlanWriter writer,
        IHolidayCalendarSource holidaySource,
        [Description("ADO delivery plan id (GUID) to resolve, e.g. from get_current_plan")] string planId,
        [Description("Preview only when true (default); apply the moves to ADO when false")] bool dryRun = true,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(planId))
            throw new ArgumentException("Provide the plan id (planId).");

        var plan = await reader.GetPlanAsync(planId, ct);
        var calendars = await holidaySource.GetCalendarAsync(MarkerYears(plan), ct);
        var conflicts = HolidayConflictDetector.Detect(plan.Events, calendars);

        if (conflicts.Count == 0)
            return new
            {
                planId,
                planName = plan.Sprint.SprintId,
                dryRun,
                applied = false,
                conflictCount = 0,
                resolutions = Array.Empty<object>(),
                message = "No holiday conflicts found; nothing to resolve."
            };

        // One resolution per conflicting marker (a marker can clash in several calendars
        // on the same day). Roll each forward to a day clear in EVERY calendar.
        var resolutions = conflicts
            .GroupBy(c => c.Marker)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var from = g.First().Date;
                var to = NextClearBusinessDay(from, calendars);
                return new
                {
                    marker = g.Key,
                    from,
                    to,
                    daysShifted = Period.Between(from, to, PeriodUnits.Days).Days,
                    holidays = g.Select(c => new
                    {
                        country = c.Country,
                        region = c.Region,
                        holiday = c.HolidayName
                    }).ToList()
                };
            })
            .ToList();

        if (dryRun)
            return new
            {
                planId,
                planName = plan.Sprint.SprintId,
                dryRun = true,
                applied = false,
                conflictCount = resolutions.Count,
                resolutions = resolutions.Select(r => new
                {
                    marker = r.marker.ToString(),
                    from = Iso(r.from),
                    to = Iso(r.to),
                    r.daysShifted,
                    r.holidays
                }).ToList(),
                message = "Preview only — re-run with dryRun=false to apply these moves to ADO."
            };

        // Apply each move in place. Each update independently re-reads the plan revision,
        // so sequential writes are safe.
        var applied = new List<object>();
        foreach (var r in resolutions)
        {
            var result = await writer.UpdateMarkerDateAsync(planId, r.marker, r.to, ct);
            applied.Add(new
            {
                marker = r.marker.ToString(),
                from = Iso(r.from),
                to = Iso(r.to),
                r.daysShifted,
                found = result.Found,
                previousDate = result.PreviousDate is { } p ? Iso(p) : null,
                r.holidays
            });
        }

        // Re-read so the caller sees the real, persisted state and fresh warnings.
        var updated = await reader.GetPlanAsync(planId, ct);

        var markers = updated.Events
            .OrderBy(e => e.Date)
            .Select(e => new
            {
                label = e.Label.ToString(),
                date = Iso(e.Date),
                adjusted = e.Adjusted,
                originalDate = e.OriginalDate is { } od ? Iso(od) : null
            })
            .ToList();

        var remaining = await WarningsFor(updated, holidaySource, ct);

        var remainingWarnings = remaining.Select(c => new
        {
            marker = c.Marker.ToString(),
            date = Iso(c.Date),
            country = c.Country,
            region = c.Region,
            holiday = c.HolidayName
        }).ToList();

        var warningsSummary = remaining
            .Select(c => $"{c.Marker} {Iso(c.Date)} falls on a public holiday in {c.Country}-{c.Region}: {c.HolidayName}")
            .ToList();

        return new
        {
            planId,
            planName = updated.Sprint.SprintId,
            dryRun = false,
            applied = true,
            resolvedCount = applied.Count(a => (bool)((dynamic)a).found),
            resolutions = applied,
            markers,
            remainingWarningsCount = remainingWarnings.Count,
            remainingWarnings,
            warningsSummary,
            orderWarnings = OrderWarnings(updated)
        };
    }

    // Rolls forward from a date to the first weekday that is not a holiday in ANY calendar.
    // Mirrors HolidayCalendar.RollForwardToBusinessDay but across every country/region set.
    internal static LocalDate NextClearBusinessDay(LocalDate date, IReadOnlyList<CountryHolidays> calendars)
    {
        var d = date;
        while (!IsClearBusinessDay(d, calendars))
            d = d.PlusDays(1);
        return d;
    }

    private static bool IsClearBusinessDay(LocalDate date, IReadOnlyList<CountryHolidays> calendars)
    {
        if (date.DayOfWeek is IsoDayOfWeek.Saturday or IsoDayOfWeek.Sunday)
            return false;
        foreach (var cal in calendars)
            if (cal.TryGet(date, out _))
                return false;
        return true;
    }

    [McpServerTool, Description(
        "Lists the public holidays loaded for a given year, WITHOUT touching any delivery plan. " +
        "Reads straight from the holiday calendars used by the engine and returns every holiday as " +
        "{ date, name, country, region }, ordered by date. Optionally filter by country (ISO code " +
        "like 'MX','US','IN') and/or region/state code. Use it to answer 'which days are holidays in " +
        "2026?' on its own. Returns count=0 when no holidays are loaded for that year.")]
    public static async Task<object> ListHolidays(
        IHolidayCalendarSource holidaySource,
        [Description("Year to list holidays for, e.g. 2026")] int year,
        [Description("Optional ISO country code filter, e.g. 'MX','US','IN'. Omit for all countries.")] string? country = null,
        [Description("Optional region/state code filter, e.g. 'KA','TG'. Omit for all regions.")] string? region = null,
        CancellationToken ct = default)
    {
        var calendars = await holidaySource.GetCalendarAsync([year], ct);

        var holidays = calendars
            .Where(c => country is null || string.Equals(c.Country, country, StringComparison.OrdinalIgnoreCase))
            .Where(c => region is null || string.Equals(c.Region, region, StringComparison.OrdinalIgnoreCase))
            .SelectMany(c => c.Holidays.Select(h => new
            {
                date = Iso(h.Date),
                name = h.Name,
                country = c.Country,
                region = c.Region
            }))
            .OrderBy(h => h.date, StringComparer.Ordinal)
            .ThenBy(h => h.country, StringComparer.Ordinal)
            .ToList();

        return new
        {
            year,
            country,
            region,
            count = holidays.Count,
            holidays
        };
    }

    [McpServerTool, Description(
        "Renders an EXISTING GFR delivery plan to the SAME branded HTML e-mail visual that the " +
        "Azure Function produces — but in-process, straight from Azure DevOps, so no Function has " +
        "to be deployed or reachable. Pass the plan id (planId) from get_current_plan or list_plans. " +
        "Returns { planId, today, contentType:'text/html', length, html } where 'html' is a complete, " +
        "self-contained document (the progress ring is embedded as a base64 PNG). This is the exact " +
        "artifact to drop into the monthly draft e-mail. Optionally pass 'today' (ISO yyyy-MM-dd) to " +
        "compute the 'days to release' countdown as of that date; defaults to today.")]
    public static async Task<object> GetPlanRender(
        IDeliveryPlanReader reader,
        IDeliveryPlanRenderer renderer,
        [Description("ADO delivery plan id (GUID) to render, e.g. from get_current_plan")] string planId,
        [Description("Reference date ISO yyyy-MM-dd for the countdown. Defaults to today.")] string? today = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(planId))
            throw new ArgumentException("Provide the plan id (planId).");

        var asOf = string.IsNullOrWhiteSpace(today)
            ? LocalDate.FromDateTime(DateTime.Today)
            : ParseDate(today, nameof(today));

        var plan = await reader.GetPlanAsync(planId, ct);
        var html = renderer.Render(plan, asOf);

        return new
        {
            planId,
            today = Iso(asOf),
            contentType = "text/html",
            length = html.Length,
            html
        };
    }

    [McpServerTool, Description(
        "Creates an Outlook DRAFT e-mail (it does NOT send it) for an EXISTING GFR delivery plan, " +
        "so it lands in the signed-in mailbox's Drafts folder for a human to review and send. " +
        "All-in-one: reads the plan from Azure DevOps, renders the SAME branded HTML as " +
        "get_plan_render, builds the subject ('[GFR] Delivery Plan — <Month> Release' for PROD " +
        "plans, '<Month> QED Deployment' for busy-season plans) and creates the draft via Microsoft " +
        "Graph. The FIRST call opens a browser for login (Mail.ReadWrite consent). Pass the plan id " +
        "(planId) from get_current_plan or list_plans; optionally override the subject and/or the " +
        "'today' reference date used for the countdown. Returns { planId, subject, created, bodyLength }.")]
    public static async Task<object> CreatePlanDraft(
        IDeliveryPlanReader reader,
        IDeliveryPlanRenderer renderer,
        IDraftCreator draftCreator,
        GraphConfig graph,
        [Description("ADO delivery plan id (GUID) to draft, e.g. from get_current_plan")] string planId,
        [Description("Optional subject override. Omit to auto-build it from the plan's goal month/kind.")] string? subject = null,
        [Description("Reference date ISO yyyy-MM-dd for the countdown. Defaults to today.")] string? today = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(planId))
            throw new ArgumentException("Provide the plan id (planId).");

        // Fail fast with a clear message instead of an opaque Graph auth error.
        if (string.IsNullOrWhiteSpace(graph.TenantId) || string.IsNullOrWhiteSpace(graph.ClientId))
            return new
            {
                planId,
                created = false,
                error = "Microsoft Graph is not configured on the MCP server: 'Graph:TenantId' " +
                        "and/or 'Graph:ClientId' are missing. Add them to the Mcp user-secrets " +
                        "(or environment) and restart the server, then retry."
            };

        var asOf = string.IsNullOrWhiteSpace(today)
            ? LocalDate.FromDateTime(DateTime.Today)
            : ParseDate(today, nameof(today));

        var plan = await reader.GetPlanAsync(planId, ct);
        var html = renderer.Render(plan, asOf);
        var finalSubject = string.IsNullOrWhiteSpace(subject) ? BuildDraftSubject(plan) : subject;

        try
        {
            await draftCreator.CreateDraftAsync(finalSubject, html, ct);
        }
        catch (Exception ex)
        {
            // Surface the REAL cause (e.g. interactive login not completed, consent
            // denied) instead of the framework's generic "An error occurred invoking".
            return new
            {
                planId,
                subject = finalSubject,
                created = false,
                error = $"Draft creation failed: {ex.Message}",
                hint = "The first call opens a browser to sign in and consent to Mail.ReadWrite. " +
                       "Make sure that sign-in completed on the machine hosting the MCP server."
            };
        }

        return new
        {
            planId,
            subject = finalSubject,
            created = true,
            bodyLength = html.Length
        };
    }

    [McpServerTool, Description(
        "Exports an EXISTING GFR delivery plan as a ready-to-send Outlook e-mail FILE (.eml), " +
        "WITHOUT Microsoft Graph, permissions or admin approval. Reads the plan from Azure DevOps, " +
        "renders the SAME branded HTML as get_plan_render, and writes a .eml with an 'X-Unsent' " +
        "header so double-clicking it opens Outlook in compose mode (an editable, sendable message " +
        "in your own mailbox). Use this when create_plan_draft is blocked by tenant admin consent. " +
        "Pass the plan id (planId); optionally set the recipient 'to', a subject override, the " +
        "'today' reference date, and an output path. Returns { planId, subject, to, path, fileUrl, " +
        "folder, folderUrl, bytesWritten, message }. When you report the result to the user, confirm " +
        "the file was saved and render 'fileUrl' as a clickable markdown link (e.g. [Open .eml](fileUrl)) " +
        "and 'folderUrl' as an 'Open folder' link, instead of showing the raw path as plain text.")]
    public static async Task<object> ExportPlanEmail(
        IDeliveryPlanReader reader,
        IDeliveryPlanRenderer renderer,
        [Description("ADO delivery plan id (GUID) to export, e.g. from get_current_plan")] string planId,
        [Description("Optional recipient e-mail for the To: line, e.g. 'you@thomsonreuters.com'. Omit to leave it blank.")] string? to = null,
        [Description("Optional subject override. Omit to auto-build it from the plan's goal month/kind.")] string? subject = null,
        [Description("Reference date ISO yyyy-MM-dd for the countdown. Defaults to today.")] string? today = null,
        [Description("Optional full output path for the .eml. Omit to save into the user's Downloads folder.")] string? outputPath = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(planId))
            throw new ArgumentException("Provide the plan id (planId).");

        var asOf = string.IsNullOrWhiteSpace(today)
            ? LocalDate.FromDateTime(DateTime.Today)
            : ParseDate(today, nameof(today));

        var plan = await reader.GetPlanAsync(planId, ct);
        var html = renderer.Render(plan, asOf);
        var finalSubject = string.IsNullOrWhiteSpace(subject) ? BuildDraftSubject(plan) : subject;

        var eml = EmlDraftWriter.BuildEml(from: null, to: to, subject: finalSubject, htmlBody: html, date: DateTimeOffset.Now);

        var path = string.IsNullOrWhiteSpace(outputPath)
            ? Path.Combine(DefaultExportDir(), SafeFileName(finalSubject) + ".eml")
            : outputPath;

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(path, eml, new System.Text.UTF8Encoding(false), ct);

        var fullPath = Path.GetFullPath(path);
        var folder = Path.GetDirectoryName(fullPath) ?? "";

        return new
        {
            planId,
            subject = finalSubject,
            to,
            path = fullPath,
            // file:// URIs the client can render as clickable links (opens the .eml / its folder).
            fileUrl = new Uri(fullPath).AbsoluteUri,
            folder,
            folderUrl = string.IsNullOrEmpty(folder) ? null : new Uri(folder).AbsoluteUri,
            bytesWritten = new FileInfo(fullPath).Length,
            message = $"Exported .eml to {fullPath}. Open it (double-click, or use the link) to review and send it from Outlook."
        };
    }

    // Downloads if it exists (most discoverable for the user), else the temp folder.
    private static string DefaultExportDir()
    {
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        return Directory.Exists(downloads) ? downloads : Path.GetTempPath();
    }

    // Strips characters that are illegal in file names so the subject can seed the file name.
    private static string SafeFileName(string subject)
    {
        var cleaned = new string(subject.Select(c => Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '-' : c).ToArray());
        return cleaned.Trim().Length == 0 ? "delivery-plan" : cleaned.Trim();
    }

    // Subject mirrors the SmokeTest e-mail path: PROD plans ship a "Release", busy-season
    // (QED-only) plans a "QED Deployment"; the month comes from that goal milestone.
    private static string BuildDraftSubject(DeliveryPlan plan)
    {
        var hasRelease = plan.Events.Any(e => e.Label == Milestone.Release);
        var goalMilestone = hasRelease ? Milestone.Release : Milestone.QedDeploy;
        var goal = plan.Events.First(e => e.Label == goalMilestone);
        var monthName = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(goal.Date.Month);
        var kind = hasRelease ? "Release" : "QED Deployment";
        return $"[GFR] Delivery Plan \u2014 {monthName} {kind}";
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