namespace Core.Tests;

// Regression tests for the plan-selection rule: the "current" plan is chosen by
// GOAL DATE (nearest >= today; fallback latest past), NOT by edit recency or list
// order. If someone reintroduces a ModifiedAt sort or "first-in-list" pick, these
// tests break.
public class CurrentPlanSelectorTests
{
    private static readonly LocalDate Today = new(2026, 8, 21);

    // Helper: build a ref. ModifiedAt defaults to "edited right now" so tests can
    // prove recency does NOT influence selection.
    private static DeliveryPlanRef Plan(string id, string name, LocalDate? goal, Instant? modifiedAt = null) =>
        new(id, name, "Moser, Mariana",
            modifiedAt ?? Instant.FromUtc(2026, 8, 21, 12, 0),
            goal);

    // THE bug this whole change exists for: a plan for a PAST month (February)
    // edited today would sort to the top by ModifiedAt and win. It must NOT.
    [Fact]
    public void Ignores_out_of_order_recent_edit_and_picks_nearest_future()
    {
        var plans = new[]
        {
            // February edited "just now" (recent) but its goal is in the past.
            Plan("feb", "[GFR][2026][Delivery Plan] - February 16th QED Release",
                 new LocalDate(2026, 2, 16), Instant.FromUtc(2026, 8, 21, 23, 59)),
            // September edited long ago; goal is future but not the nearest.
            Plan("sep", "[GFR][2026][Delivery Plan] - September 14th QED Release",
                 new LocalDate(2026, 9, 14), Instant.FromUtc(2026, 1, 1, 0, 0)),
            // August: the nearest future goal (24 Aug >= 21 Aug).
            Plan("aug", "[GFR][2026][Delivery Plan] - August 24th Release",
                 new LocalDate(2026, 8, 24), Instant.FromUtc(2026, 1, 1, 0, 0)),
        };

        var picked = CurrentPlanSelector.Pick(plans, Today);

        Assert.NotNull(picked);
        Assert.Equal("aug", picked!.Id); // nearest future wins; February is ignored
    }

    [Fact]
    public void Picks_the_nearest_future_when_several_are_ahead()
    {
        var plans = new[]
        {
            Plan("sep", "[GFR][2026][Delivery Plan] - September 14th QED Release", new LocalDate(2026, 9, 14)),
            Plan("oct", "[GFR][2026][Delivery Plan] - October 27th Release",      new LocalDate(2026, 10, 27)),
            Plan("aug", "[GFR][2026][Delivery Plan] - August 24th Release",       new LocalDate(2026, 8, 24)),
        };

        var picked = CurrentPlanSelector.Pick(plans, Today);

        Assert.Equal("aug", picked!.Id);
    }

    [Fact]
    public void Includes_a_plan_whose_goal_is_exactly_today()
    {
        var plans = new[]
        {
            Plan("today", "[GFR][2026][Delivery Plan] - August 21st Release", new LocalDate(2026, 8, 21)),
            Plan("sep",   "[GFR][2026][Delivery Plan] - September 14th QED Release", new LocalDate(2026, 9, 14)),
        };

        var picked = CurrentPlanSelector.Pick(plans, Today);

        Assert.Equal("today", picked!.Id); // >= today is inclusive
    }

    [Fact]
    public void Falls_back_to_latest_past_when_no_future_plan_exists()
    {
        var plans = new[]
        {
            Plan("jun", "[GFR][2026][Delivery Plan] - June 22th Release", new LocalDate(2026, 6, 22)),
            Plan("jul", "[GFR][2026][Delivery Plan] - July 27th Release", new LocalDate(2026, 7, 27)),
        };

        var picked = CurrentPlanSelector.Pick(plans, Today);

        Assert.Equal("jul", picked!.Id); // latest past
    }

    [Fact]
    public void Skips_plans_with_no_parseable_goal_date()
    {
        var plans = new[]
        {
            Plan("weird", "Some non-GFR plan without a date", goal: null),
            Plan("aug",   "[GFR][2026][Delivery Plan] - August 24th Release", new LocalDate(2026, 8, 24)),
        };

        var picked = CurrentPlanSelector.Pick(plans, Today);

        Assert.Equal("aug", picked!.Id);
    }

    [Fact]
    public void Returns_null_when_no_plan_has_a_goal_date()
    {
        var plans = new[]
        {
            Plan("a", "No date here", goal: null),
            Plan("b", "Also no date", goal: null),
        };

        var picked = CurrentPlanSelector.Pick(plans, Today);

        Assert.Null(picked);
    }

    [Fact]
    public void Returns_null_for_an_empty_list()
    {
        var picked = CurrentPlanSelector.Pick(System.Array.Empty<DeliveryPlanRef>(), Today);
        Assert.Null(picked);
    }
}