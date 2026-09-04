using System.Collections;
using Adapters;
using Core.Application;
using Core.Ports;
using static Mcp.Tests.PlanFactory;

namespace Mcp.Tests;

// Exercises the list_plans tool over an in-memory catalog fake, verifying the
// year filter, goal-date ordering (undated last), the isCurrent / currentPlanId
// flag and the passthrough filter — all without touching the network.
public class ListPlansTests
{
    private static readonly Instant Mod = Instant.FromUtc(2026, 1, 1, 0, 0);

    private static readonly AdoConfig Ado = new(
        Organization: "tr-tax", Project: "TaxProf", Team: "GFR",
        Pat: "x", TeamIds: Array.Empty<string>());

    private static DeliveryPlanRef Ref(string id, string name, string owner, LocalDate? goal) =>
        new(id, name, owner, Mod, goal);

    private sealed class FakeCatalog(params DeliveryPlanRef[] plans) : IDeliveryPlanCatalog
    {
        public string? LastFilter { get; private set; }
        public Task<IReadOnlyList<DeliveryPlanRef>> FindPlansAsync(string textFilter, CancellationToken ct = default)
        {
            LastFilter = textFilter;
            return Task.FromResult<IReadOnlyList<DeliveryPlanRef>>(plans);
        }
    }

    private static List<object> Plans(dynamic result)
    {
        var list = new List<object>();
        foreach (var p in (IEnumerable)result.plans)
            list.Add(p);
        return list;
    }

    [Fact]
    public async Task OrdersByGoalDate_UndatedLast()
    {
        var catalog = new FakeCatalog(
            Ref("C", "[GFR][2026][Delivery Plan] - March 16th QED Release", "x", D(2026, 3, 16)),
            Ref("A", "[GFR][2026][Delivery Plan] - January 12th QED Release", "x", D(2026, 1, 12)),
            Ref("Z", "not a plan name", "x", null),
            Ref("B", "[GFR][2026][Delivery Plan] - February 9th QED Release", "x", D(2026, 2, 9)));

        dynamic result = await PlanTools.ListPlans(catalog, Ado, asOf: "2026-01-01");

        List<object> plans = Plans(result);
        var ids = plans.Select(p => (string)((dynamic)p).id).ToList();
        Assert.Equal(new[] { "A", "B", "C", "Z" }, ids);
        Assert.Equal(4, (int)result.count);
        Assert.Equal("[GFR]", catalog.LastFilter);
    }

    [Fact]
    public async Task FlagsCurrentPlan_NearestGoalOnOrAfterAsOf()
    {
        var catalog = new FakeCatalog(
            Ref("jan", "[GFR][2026][Delivery Plan] - January 12th QED Release", "x", D(2026, 1, 12)),
            Ref("mar", "[GFR][2026][Delivery Plan] - March 16th QED Release", "x", D(2026, 3, 16)),
            Ref("sep", "[GFR][2026][Delivery Plan] - September 14th QED Release", "x", D(2026, 9, 14)));

        dynamic result = await PlanTools.ListPlans(catalog, Ado, asOf: "2026-02-01");

        Assert.Equal("mar", (string)result.currentPlanId);          // nearest goal >= 2026-02-01
        List<object> plans = Plans(result);
        var current = plans.Single(p => (bool)((dynamic)p).isCurrent);
        Assert.Equal("mar", (string)((dynamic)current).id);
        Assert.Equal(1, plans.Count(p => (bool)((dynamic)p).isCurrent)); // exactly one flagged
    }

    [Fact]
    public async Task YearFilter_NarrowsToThatYearsGoals()
    {
        var catalog = new FakeCatalog(
            Ref("a25", "[GFR][2025][Delivery Plan] - December 1st Release", "x", D(2025, 12, 1)),
            Ref("a26", "[GFR][2026][Delivery Plan] - March 16th QED Release", "x", D(2026, 3, 16)),
            Ref("nod", "no date", "x", null));

        dynamic result = await PlanTools.ListPlans(catalog, Ado, year: 2026);

        List<object> plans = Plans(result);
        var ids = plans.Select(p => (string)((dynamic)p).id).ToList();
        Assert.Equal(new[] { "a26" }, ids);          // 2025 and undated dropped
        Assert.Equal(1, (int)result.count);
        Assert.Equal(2026, (int)result.year);
    }

    [Fact]
    public async Task ProjectsIdentityFields()
    {
        var catalog = new FakeCatalog(
            Ref("id-1", "[GFR][2026][Delivery Plan] - March 16th QED Release", "Mariana Moser", D(2026, 3, 16)));

        dynamic result = await PlanTools.ListPlans(catalog, Ado, asOf: "2026-01-01");

        List<object> plans = Plans(result);
        dynamic p = plans.Single();
        Assert.Equal("id-1", (string)p.id);
        Assert.Equal("Mariana Moser", (string)p.owner);
        Assert.Equal("2026-03-16", (string)p.goalDate);
        Assert.StartsWith("2026-01-01T00:00:00", (string)p.modifiedAt);
        Assert.Equal("https://dev.azure.com/tr-tax/TaxProf/_deliveryplans/plan/id-1", (string)p.url);
        Assert.Equal("[View](https://dev.azure.com/tr-tax/TaxProf/_deliveryplans/plan/id-1)", (string)p.view);
    }

    [Fact]
    public async Task EmptyCatalog_ReturnsEmptyList_NoCurrent()
    {
        var catalog = new FakeCatalog();

        dynamic result = await PlanTools.ListPlans(catalog, Ado, filter: "[GFR][2099]");

        Assert.Equal(0, (int)result.count);
        Assert.Null((string?)result.currentPlanId);
        Assert.Empty(Plans(result));
        Assert.Equal("[GFR][2099]", (string)result.filter);
    }
}
