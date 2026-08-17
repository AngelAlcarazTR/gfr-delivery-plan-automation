namespace Functions;   // <-- CHANGE this to the SAME namespace as your RingImage.cs

// ============================================================
//  GET /api/plan  — Azure SHELL (thin, disposable).
//  It only parses the request and calls the agnostic DeliveryPlanJob.
//
//  Optional query params:
//     ?qed=2026-07-13       QED anchor      (default: July golden, verified)
//     ?release=2026-07-27   Release anchor  (default: July golden)
//     ?kind=prod|qedonly    plan type       (default: prod)
//
//  Examples:
//     /api/plan                                    -> July golden (dates in the past)
//     /api/plan?qed=2026-10-12&release=2026-10-26  -> future plan (shows the countdown)
//     /api/plan?qed=2026-02-09&kind=qedonly        -> month with no Release (QED only)
// ============================================================
public class PlanEndpoint(ILogger<PlanEndpoint> logger)
{
    private readonly ILogger<PlanEndpoint> _logger = logger;
    private static readonly LocalDatePattern Iso = LocalDatePattern.Iso;

    [Function("Plan")]
    public IActionResult Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "plan")] HttpRequest req)
    {
        var qed = ParseDate(req.Query["qed"].ToString(), new LocalDate(2026, 7, 13));
        var kind = string.Equals(req.Query["kind"].ToString(), "qedonly", StringComparison.OrdinalIgnoreCase)
                     ? PlanKind.QedOnly : PlanKind.Prod;
        LocalDate? release = kind == PlanKind.Prod
                     ? ParseDate(req.Query["release"].ToString(), new LocalDate(2026, 7, 27))
                     : null;

        _logger.LogInformation("Generating plan: QED={Qed} Release={Rel} Kind={Kind}", qed, release, kind);

        var schedule = new ReleaseSchedule(kind, qed, release, PlanName: "POC");

        // The shell wires the agnostic job with the concrete renderer and invokes it.
        var job = new DeliveryPlanJob(new HtmlEmailRenderer());
        var html = job.GenerateHtml(schedule, LocalDate.FromDateTime(DateTime.Today));

        return new ContentResult
        {
            Content = html,
            ContentType = "text/html; charset=utf-8",
            StatusCode = 200
        };
    }

    private static LocalDate ParseDate(string? s, LocalDate fallback)
    {
        if (string.IsNullOrWhiteSpace(s)) return fallback;
        var r = Iso.Parse(s);
        return r.Success ? r.Value : fallback;
    }
}