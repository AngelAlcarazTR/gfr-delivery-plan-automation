namespace Functions;

// POST /api/plan-year/compute
// Given the year's anchors (in the body), runs the engine per month and returns
// the computed plans as JSON. COMPUTE ONLY — it never writes to ADO.
//
// Holidays now come from the Blob (IHolidayReader) so dates use the OFFICIAL TR
// dates (e.g. Independence Day = Jul 3 observed). Falls back to CompanyHolidays
// when the year's blob is missing.
public class PlanYearComputeEndpoint(
    ILogger<PlanYearComputeEndpoint> logger,
    IHolidayReader holidayReader)
{
    private readonly ILogger<PlanYearComputeEndpoint> _logger = logger;
    private readonly IHolidayReader _holidayReader = holidayReader;

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

        // Holidays for the year: from the Blob (official dates) or fallback to rules.
        var holidayDates = await _holidayReader.GetHolidaysAsync(request.Year, ct);
        var calendar = holidayDates is not null
            ? new HolidayCalendar(holidayDates)
            : CompanyHolidays.Calendar(request.Year, request.Year + 1);

        _logger.LogInformation("Holidays for {Year}: {Source}",
            request.Year, holidayDates is not null ? "Blob" : "fallback CompanyHolidays");

        var plans = new List<ComputedPlan>();
        var errors = new List<ComputeError>();

        foreach (var a in request.Anchors)
        {
            try
            {
                var schedule = BuildSchedule(request.Year, a);
                var plan = DeliveryPlanJob.BuildPlan(schedule, calendar);   // <-- pass holidays

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

    private static ReleaseSchedule BuildSchedule(int year, AnchorInput a)
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