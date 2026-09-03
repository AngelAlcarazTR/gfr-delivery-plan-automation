using static Mcp.Tests.PlanFactory;

namespace Mcp.Tests;

public class ApplyOverridesTests
{
    [Fact]
    public void NullOverrides_ReturnsSameInstance()
    {
        var plan = InOrderPlan();
        var result = PlanTools.ApplyOverrides(plan, null);
        Assert.Same(plan, result);
    }

    [Fact]
    public void EmptyOverrides_ReturnsSameInstance()
    {
        var plan = InOrderPlan();
        var result = PlanTools.ApplyOverrides(plan, Array.Empty<MarkerOverride>());
        Assert.Same(plan, result);
    }

    [Fact]
    public void SingleOverride_MovesMarker_SetsAdjustedAndOriginalDate()
    {
        var plan = InOrderPlan();
        var original = plan.Event(Milestone.StartDev).Date;

        var result = PlanTools.ApplyOverrides(plan,
            [new MarkerOverride("StartDev", "2026-08-21")]);

        var moved = result.Event(Milestone.StartDev);
        Assert.Equal(D(2026, 8, 21), moved.Date);
        Assert.True(moved.Adjusted);
        Assert.Equal(original, moved.OriginalDate);
    }

    [Fact]
    public void Override_DoesNotTouchOtherMarkers()
    {
        var plan = InOrderPlan();

        var result = PlanTools.ApplyOverrides(plan,
            [new MarkerOverride("StartDev", "2026-08-21")]);

        foreach (var e in result.Events.Where(e => e.Label != Milestone.StartDev))
        {
            Assert.False(e.Adjusted);
            Assert.Null(e.OriginalDate);
        }
    }

    [Fact]
    public void MultipleOverrides_AllApplied()
    {
        var plan = InOrderPlan();

        var result = PlanTools.ApplyOverrides(plan,
        [
            new MarkerOverride("StartDev", "2026-08-21"),
            new MarkerOverride("QedDeploy", "2026-09-15"),
        ]);

        Assert.Equal(D(2026, 8, 21), result.Event(Milestone.StartDev).Date);
        Assert.True(result.Event(Milestone.StartDev).Adjusted);
        Assert.Equal(D(2026, 9, 15), result.Event(Milestone.QedDeploy).Date);
        Assert.True(result.Event(Milestone.QedDeploy).Adjusted);
    }

    [Fact]
    public void Override_PreservesEventOrder()
    {
        var plan = InOrderPlan();
        var expectedOrder = plan.Events.Select(e => e.Label).ToArray();

        var result = PlanTools.ApplyOverrides(plan,
            [new MarkerOverride("QedDeploy", "2026-09-15")]);

        Assert.Equal(expectedOrder, result.Events.Select(e => e.Label).ToArray());
    }

    [Fact]
    public void Override_IsCaseInsensitive()
    {
        var plan = InOrderPlan();

        var result = PlanTools.ApplyOverrides(plan,
            [new MarkerOverride("startdev", "2026-08-21")]);

        Assert.Equal(D(2026, 8, 21), result.Event(Milestone.StartDev).Date);
        Assert.True(result.Event(Milestone.StartDev).Adjusted);
    }

    [Fact]
    public void Override_PreservesFirstOriginalDate_WhenAlreadyAdjusted()
    {
        var firstOriginal = D(2026, 8, 3);
        var alreadyAdjusted = new DeliveryPlan(
            new Sprint(D(2026, 1, 1), "S-TEST"),
            [new PlanEvent(Milestone.StartDev, D(2026, 8, 21), Adjusted: true, OriginalDate: firstOriginal)]);

        var result = PlanTools.ApplyOverrides(alreadyAdjusted,
            [new MarkerOverride("StartDev", "2026-08-24")]);

        var moved = result.Event(Milestone.StartDev);
        Assert.Equal(D(2026, 8, 24), moved.Date);
        Assert.Equal(firstOriginal, moved.OriginalDate);
    }

    [Fact]
    public void UnknownMarker_Throws()
    {
        var plan = InOrderPlan();

        var ex = Assert.Throws<ArgumentException>(() =>
            PlanTools.ApplyOverrides(plan, [new MarkerOverride("Foo", "2026-08-21")]));
        Assert.Contains("Foo", ex.Message);
    }

    [Fact]
    public void MarkerNotPresentInPlan_Throws()
    {
        // Plan is missing Release, so an override targeting it must fail.
        var plan = Plan(
            (Milestone.StartDev, D(2026, 8, 3)),
            (Milestone.QedDeploy, D(2026, 9, 11)));

        Assert.Throws<ArgumentException>(() =>
            PlanTools.ApplyOverrides(plan, [new MarkerOverride("Release", "2026-09-30")]));
    }

    [Fact]
    public void InvalidDate_Throws()
    {
        var plan = InOrderPlan();

        Assert.Throws<ArgumentException>(() =>
            PlanTools.ApplyOverrides(plan, [new MarkerOverride("StartDev", "not-a-date")]));
    }

    [Fact]
    public void DoesNotMutateOriginalPlan()
    {
        var plan = InOrderPlan();
        var originalDate = plan.Event(Milestone.StartDev).Date;

        _ = PlanTools.ApplyOverrides(plan, [new MarkerOverride("StartDev", "2026-08-21")]);

        Assert.Equal(originalDate, plan.Event(Milestone.StartDev).Date);
        Assert.False(plan.Event(Milestone.StartDev).Adjusted);
    }
}
