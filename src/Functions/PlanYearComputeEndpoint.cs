namespace Functions;

// POST /api/plan-year/compute
// Given the year's anchors (in the body), runs the engine per month and returns
// the computed plans as JSON. COMPUTE ONLY — it never writes to ADO.
//
// Purpose: backtesting / preview. Feed real 2025/2026 anchors and compare the
// engine output against the real plans. The write path (create the 12 in ADO)
// is a separate endpoint (plan-year/create) that will reuse this same engine.
//
// NOTE: computed dates carry the engine's current rules, including the open
// items pending Félix (End Reg -3/-4, Start Dev QED-only -20/-15, Jul-4 vs Jul-3).
// That is the point of this endpoint — to SEE those, not to hide them.
public class PlanYearComputeEndpoint(ILogger<PlanYearComputeEndpoint> logger)
{
    private readonly ILogger<PlanYearComputeEndpoint> _logger = logger;

    // Business rule (VERIFIED vs 2025 & 2026 GoFileRoom calendars): during peak
    // tax-season criticality GFR does NOT touch production, so these months are
    // QED-only (no Release):
    //   Jan-Mar -> individual + S-Corp/partnership deadlines (Mar 15 / Apr 15 lead-up)
    //   Sep     -> estimated-tax (Sep 15) + extension deadline lead-up (Oct 15)
    // Fixed by rule. Kept as a set so it can move to config if the rule ever changes.
    private static readonly int[] BusySeasonMonths = { 1, 2, 3, 9 };

    private static bool IsBusySeason(int month) => Array.IndexOf(BusySeasonMonths, month) >= 0;

    [Function("PlanYearCompute")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "plan-year/compute")] HttpRequest req,
        CancellationToken ct)
    {
        PlanYearComputeRequest? request;
        try
        {
            request = await req.ReadFromJsonAsync<PlanYearComputeRequest>(ct);
        }
        catch (Exception ex)
        {
            return new BadRequestObjectResult($"Invalid JSON body: {ex.Message}");
        }

        if (request is null || request.Anchors is null || request.Anchors.Count == 0)
            return new BadRequestObjectResult("Body must include 'year' and a non-empty 'anchors' array.");

        var plans = new List<ComputedPlan>();
        var errors = new List<ComputeError>();

        foreach (var a in request.Anchors)
        {
            try
            {
                var schedule = BuildSchedule(request.Year, a);
                var plan = DeliveryPlanJob.BuildPlan(schedule);   // engine only, no rendering, no ADO

                var markers = plan.Events
                    .Select(e => new ComputedMarker(e.Label.ToString(), Iso(e.Date)))
                    .ToList();

                plans.Add(new ComputedPlan(a.Month, schedule.Kind.ToString(), schedule.PlanName, markers));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Compute failed for month {Month}", a.Month);
                errors.Add(new ComputeError(a.Month, ex.Message));
            }
        }

        _logger.LogInformation("Computed {Count} plans for {Year} ({Errors} errors)",
            plans.Count, request.Year, errors.Count);

        return new OkObjectResult(new PlanYearComputeResponse(
            request.Year, plans.Count, plans, errors));
    }

    // Builds a ReleaseSchedule from one month's anchor input.
    // Kind is INFERRED from the busy-season rule, not taken from the input.
    private static ReleaseSchedule BuildSchedule(int year, AnchorInput a)
    {
        if (a.Month is < 1 or > 12)
            throw new ArgumentException($"Invalid month '{a.Month}'.");

        var kind = IsBusySeason(a.Month) ? PlanKind.QedOnly : PlanKind.Prod;
        var qed = ParseDate(a.Qed, nameof(a.Qed));
        LocalDate? release = string.IsNullOrWhiteSpace(a.Release)
            ? null
            : ParseDate(a.Release!, nameof(a.Release));

        // Validate the input against the rule (fail clearly on contradiction).
        if (kind == PlanKind.QedOnly && release is not null)
            throw new ArgumentException(
                $"Month {a.Month} is busy season (QED-only) and must not have a 'release' date.");
        if (kind == PlanKind.Prod && release is null)
            throw new ArgumentException(
                $"Month {a.Month} is a production month and requires a 'release' date.");

        var planName = BuildPlanName(year, kind, release ?? qed);
        return new ReleaseSchedule(kind, qed, release, planName);
    }

    // "[GFR][2026][Delivery Plan] - April 27th Release" / "... September 14th QED Release"
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