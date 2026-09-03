namespace Mcp.Tests;

/// <summary>
/// Test helpers to build in-memory <see cref="DeliveryPlan"/> instances without
/// running the deterministic engine, so the override / ordering helpers can be
/// exercised in isolation.
/// </summary>
internal static class PlanFactory
{
    private static readonly Sprint DummySprint = new(new LocalDate(2026, 1, 1), "S-TEST");

    public static LocalDate D(int year, int month, int day) => new(year, month, day);

    /// <summary>Builds a plan from label/date pairs; every event is unadjusted.</summary>
    public static DeliveryPlan Plan(params (Milestone Label, LocalDate Date)[] events)
    {
        var list = events
            .Select(e => new PlanEvent(e.Label, e.Date, Adjusted: false, OriginalDate: null))
            .ToList();
        return new DeliveryPlan(DummySprint, list);
    }

    /// <summary>A well-formed, in-order plan covering all seven milestones.</summary>
    public static DeliveryPlan InOrderPlan() => Plan(
        (Milestone.StartDev, D(2026, 8, 3)),
        (Milestone.EndDev, D(2026, 8, 28)),
        (Milestone.QaCutoff, D(2026, 9, 4)),
        (Milestone.QedDeploy, D(2026, 9, 11)),
        (Milestone.StartReg, D(2026, 9, 14)),
        (Milestone.EndReg, D(2026, 9, 25)),
        (Milestone.Release, D(2026, 9, 30)));

    public static PlanEvent Event(this DeliveryPlan plan, Milestone label) =>
        plan.Events.Single(e => e.Label == label);
}
