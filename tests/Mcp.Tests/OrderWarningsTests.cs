using static Mcp.Tests.PlanFactory;

namespace Mcp.Tests;

public class OrderWarningsTests
{
    [Fact]
    public void InOrderPlan_ReturnsEmpty()
    {
        var warnings = PlanTools.OrderWarnings(InOrderPlan());
        Assert.Empty(warnings);
    }

    [Fact]
    public void OneInversion_ReturnsSingleWarning_WithExpectedText()
    {
        // QedDeploy nudged before its predecessor QaCutoff (2026-09-04).
        var plan = PlanTools.ApplyOverrides(InOrderPlan(),
            [new MarkerOverride("QedDeploy", "2026-09-01")]);

        var warnings = PlanTools.OrderWarnings(plan);

        var w = Assert.Single(warnings);
        Assert.Equal("QedDeploy 2026-09-01 is before QaCutoff 2026-09-04", w);
    }

    [Fact]
    public void Warning_ReferencesImmediatelyPreviousMilestone()
    {
        // StartReg pushed before QaCutoff: the immediate predecessor is QedDeploy.
        var plan = PlanTools.ApplyOverrides(InOrderPlan(),
            [new MarkerOverride("StartReg", "2026-09-10")]);

        var warnings = PlanTools.OrderWarnings(plan);

        var w = Assert.Single(warnings);
        Assert.Equal("StartReg 2026-09-10 is before QedDeploy 2026-09-11", w);
    }

    [Fact]
    public void EqualAdjacentDates_ProduceNoWarning()
    {
        // Move StartReg onto the exact QedDeploy date (2026-09-11): equal, not "before".
        var plan = PlanTools.ApplyOverrides(InOrderPlan(),
            [new MarkerOverride("StartReg", "2026-09-11")]);

        Assert.Empty(PlanTools.OrderWarnings(plan));
    }

    [Fact]
    public void MultipleInversions_ReturnsMultipleWarnings()
    {
        // Push Release before EndReg AND QedDeploy before QaCutoff.
        var plan = PlanTools.ApplyOverrides(InOrderPlan(),
        [
            new MarkerOverride("QedDeploy", "2026-09-01"),
            new MarkerOverride("Release", "2026-09-20"),
        ]);

        var warnings = PlanTools.OrderWarnings(plan);

        Assert.Equal(2, warnings.Count);
        Assert.Contains("QedDeploy 2026-09-01 is before QaCutoff 2026-09-04", warnings);
        Assert.Contains("Release 2026-09-20 is before EndReg 2026-09-25", warnings);
    }

    [Fact]
    public void EventsSuppliedOutOfEnumOrder_AreStillSortedBeforeComparing()
    {
        // Feed events in a scrambled order; a correctly-dated plan must yield no warnings.
        var scrambled = Plan(
            (Milestone.Release, D(2026, 9, 30)),
            (Milestone.StartDev, D(2026, 8, 3)),
            (Milestone.QedDeploy, D(2026, 9, 11)),
            (Milestone.EndDev, D(2026, 8, 28)),
            (Milestone.StartReg, D(2026, 9, 14)),
            (Milestone.QaCutoff, D(2026, 9, 4)),
            (Milestone.EndReg, D(2026, 9, 25)));

        Assert.Empty(PlanTools.OrderWarnings(scrambled));
    }

    [Fact]
    public void FirstMilestoneMovedLate_FlagsNextMilestone()
    {
        // StartDev pushed past EndDev: the inversion is detected on EndDev vs StartDev.
        var plan = PlanTools.ApplyOverrides(InOrderPlan(),
            [new MarkerOverride("StartDev", "2026-08-29")]);

        var warnings = PlanTools.OrderWarnings(plan);

        var w = Assert.Single(warnings);
        Assert.Equal("EndDev 2026-08-28 is before StartDev 2026-08-29", w);
    }
}
