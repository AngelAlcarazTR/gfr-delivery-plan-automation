namespace Functions;

// POST /api/plan-year/create
// Computes plans from anchors AND writes them to ADO (create). Idempotent:
// a plan whose name already exists is SKIPPED (never duplicated).
//
// Reuses the same engine as plan-year/compute; adds tag generation (verified
// rule below) + existence check (catalog) + write (writer).
//
// Tag rule (VERIFIED against 4 busy months + Prod months in real ADO):
//   Prod month : ["{year}.{mm}"]
//   Busy month : ["{year}.{mm}_QED", "{year}.{next Prod mm}"]
//   next Prod   = first month after this one that is NOT busy ({1,2,3,9})
public class PlanYearCreateEndpoint(
    ILogger<PlanYearCreateEndpoint> logger,
    IHolidayReader holidayReader,
    IDeliveryPlanCatalog catalog,
    IDeliveryPlanWriter writer)
{
    private readonly ILogger<PlanYearCreateEndpoint> _logger = logger;
    private readonly IHolidayReader _holidayReader = holidayReader;
    private readonly IDeliveryPlanCatalog _catalog = catalog;
    private readonly IDeliveryPlanWriter _writer = writer;

    private static readonly int[] BusySeasonMonths = { 1, 2, 3, 9 };
    private static bool IsBusySeason(int month) => Array.IndexOf(BusySeasonMonths, month) >= 0;

    [Function("PlanYearCreate")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "plan-year/create")] HttpRequest req,
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

        // Holidays (Blob or fallback), same as compute.
        var holidayDates = await _holidayReader.GetHolidaysAsync(request.Year, ct);
        var calendar = holidayDates is not null
            ? new HolidayCalendar(holidayDates)
            : CompanyHolidays.Calendar(request.Year, request.Year + 1);

        // Existing plan names (to skip duplicates). Pull the GFR plans once.
        var existing = await _catalog.FindPlansAsync("[GFR]", ct);
        var existingNames = existing.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var created = new List<object>();
        var skipped = new List<object>();
        var errors = new List<object>();

        foreach (var a in request.Anchors)
        {
            try
            {
                var schedule = BuildSchedule(request.Year, a);
                var plan = DeliveryPlanJob.BuildPlan(schedule, calendar);

                if (existingNames.Contains(schedule.PlanName))
                {
                    skipped.Add(new { month = a.Month, planName = schedule.PlanName, reason = "already exists" });
                    continue;
                }

                var tags = TagsFor(request.Year, a.Month);
                var options = new PlanPublishOptions(schedule.PlanName, tags);

                var refCreated = await _writer.CreateAsync(plan, options, ct);
                created.Add(new { month = a.Month, planId = refCreated.Id, planName = refCreated.Name, tags });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Create failed for month {Month}", a.Month);
                errors.Add(new { month = a.Month, reason = ex.Message });
            }
        }

        _logger.LogInformation("Create {Year}: {Created} created, {Skipped} skipped, {Errors} errors",
            request.Year, created.Count, skipped.Count, errors.Count);

        return new OkObjectResult(new
        {
            year = request.Year,
            createdCount = created.Count,
            skippedCount = skipped.Count,
            errorCount = errors.Count,
            created,
            skipped,
            errors
        });
    }

    // Tag rule — VERIFIED against real ADO (Jan/Feb/Mar/Sep busy + May Prod).
    private static IReadOnlyList<string> TagsFor(int year, int month)
    {
        if (!IsBusySeason(month))
            return new[] { $"{year}.{month:D2}" };

        var next = NextProdMonth(month);
        return [$"{year}.{month:D2}_QED", $"{year}.{next:D2}"];
    }

    // First month after 'month' that is NOT busy. (Within the same year for the
    // known cases; a year-boundary case would need its own handling.)
    private static int NextProdMonth(int month)
    {
        var m = month + 1;
        while (m <= 12 && IsBusySeason(m)) m++;
        return m;
    }

    private static ReleaseSchedule BuildSchedule(int year, AnchorInput a)
    {
        if (a.Month is < 1 or > 12)
            throw new ArgumentException($"Invalid month '{a.Month}'.");

        var kind = IsBusySeason(a.Month) ? PlanKind.QedOnly : PlanKind.Prod;
        var qed = ParseDate(a.Qed, nameof(a.Qed));
        LocalDate? release = string.IsNullOrWhiteSpace(a.Release)
            ? null
            : ParseDate(a.Release!, nameof(a.Release));

        if (kind == PlanKind.QedOnly && release is not null)
            throw new ArgumentException(
                $"Month {a.Month} is busy season (QED-only) and must not have a 'release' date.");
        if (kind == PlanKind.Prod && release is null)
            throw new ArgumentException(
                $"Month {a.Month} is a production month and requires a 'release' date.");

        var planName = BuildPlanName(year, kind, release ?? qed);
        return new ReleaseSchedule(kind, qed, release, planName);
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
}