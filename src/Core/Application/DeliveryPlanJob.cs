namespace Core.Application;

// ============================================================
//  Cloud-agnostic use case — knows nothing about Azure or AWS.
//  Given one or two anchors (QED / Release), it computes the plan
//  with the engine and renders it to HTML via the renderer port.
//
//  Any cloud "shell" (Azure Function, AWS Lambda, a Timer, an HTTP
//  endpoint) only has to INVOKE this job.
//  The day it moves to AWS, this class is reused AS-IS.
// ============================================================
public sealed class DeliveryPlanJob(IDeliveryPlanRenderer renderer)
{
    private readonly IDeliveryPlanRenderer _renderer = renderer;

    // Computes the plan only (no rendering). Handy for validating/comparing against ADO later.
    public static DeliveryPlan BuildPlan(ReleaseSchedule schedule, HolidayCalendar? holidays = null)
    {
        // If no calendar is supplied, use the company holidays for the QED's year (+ the next one).
        holidays ??= CompanyHolidays.Calendar(schedule.QedDeploy.Year, schedule.QedDeploy.Year + 1);
        return ReleaseAnchoredCalculator.Compute(schedule, holidays);
    }

    // Computes the plan and returns the email HTML.
    public string GenerateHtml(ReleaseSchedule schedule, LocalDate today, HolidayCalendar? holidays = null)
        => _renderer.Render(BuildPlan(schedule, holidays), today);

    // Reads a plan straight from the delivery-plan source (ADO today) and renders it.
    // Takes the PORTS, not concrete adapters — stays cloud- and source-agnostic.
    public async Task<string> GenerateFromSourceAsync(
        IDeliveryPlanCatalog catalog,
        IDeliveryPlanReader reader,
        string planFilter,
        LocalDate today,
        CancellationToken ct = default)
    {
        // 1) Find candidate plans matching the filter (owner or name).
        var matches = await catalog.FindPlansAsync(planFilter, ct);

        // 2) Pick the first plan that has at least the QED marker (Prod or QedOnly).
        foreach (var candidate in matches)
        {
            var plan = await reader.GetPlanAsync(candidate.Id, ct);
            if (plan.Events.Any(e => e.Label == Milestone.QedDeploy))
                return _renderer.Render(plan, today);
        }

        throw new InvalidOperationException(
            $"No complete plan found for filter '{planFilter}'.");
    }
}