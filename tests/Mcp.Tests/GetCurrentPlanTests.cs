using System.Collections;
using Core.Application;
using Core.Ports;
using static Mcp.Tests.PlanFactory;

namespace Mcp.Tests;

// Exercises the get_current_plan tool over in-memory fakes for the ADO ports and
// the holiday source, so the selection (CurrentPlanSelector), marker mapping,
// holiday warnings and order warnings are verified without touching the network.
public class GetCurrentPlanTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 1, 1, 0, 0);

    private static DeliveryPlanRef Ref(string id, string name, string owner, LocalDate? goal) =>
        new(id, name, owner, Now, goal);

    // --- fakes -------------------------------------------------------------

    private sealed class FakeCatalog(params DeliveryPlanRef[] plans) : IDeliveryPlanCatalog
    {
        public string? LastFilter { get; private set; }
        public Task<IReadOnlyList<DeliveryPlanRef>> FindPlansAsync(string textFilter, CancellationToken ct = default)
        {
            LastFilter = textFilter;
            return Task.FromResult<IReadOnlyList<DeliveryPlanRef>>(plans);
        }
    }

    private sealed class FakeReader(Dictionary<string, DeliveryPlan> byId) : IDeliveryPlanReader
    {
        public string? LastId { get; private set; }
        public Task<DeliveryPlan> GetPlanAsync(string planId, CancellationToken ct = default)
        {
            LastId = planId;
            return Task.FromResult(byId[planId]);
        }
    }

    private sealed class FakeHolidaySource(params CountryHolidays[] calendars) : IHolidayCalendarSource
    {
        public Task<IReadOnlyList<CountryHolidays>> GetCalendarAsync(IEnumerable<int> years, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CountryHolidays>>(calendars);
    }

    // --- helpers -----------------------------------------------------------

    private static List<string> MarkerDates(dynamic result)
    {
        var dates = new List<string>();
        foreach (var m in (IEnumerable)result.markers)
            dates.Add((string)((dynamic)m).date);
        return dates;
    }

    // --- tests -------------------------------------------------------------

    [Fact]
    public async Task FoundPlan_ReturnsIdentityAndAllMarkers()
    {
        var planRef = Ref("A", "[GFR][2026][Delivery Plan] - September 5th QED Release", "Angel Alcaraz", D(2026, 9, 5));
        var catalog = new FakeCatalog(planRef);
        var reader = new FakeReader(new() { ["A"] = InOrderPlan() });

        dynamic result = await PlanTools.GetCurrentPlan(
            catalog, reader, new FakeHolidaySource(), asOf: "2026-09-03");

        Assert.True((bool)result.found);
        Assert.Equal("A", (string)result.planId);            // falls back to ref id when plan has none
        Assert.Equal(planRef.Name, (string)result.planName);
        Assert.Equal("Angel Alcaraz", (string)result.owner);
        Assert.Equal("2026-09-05", (string)result.goalDate);
        Assert.Equal("2026-09-03", (string)result.asOf);
        Assert.Equal("A", reader.LastId);
        List<string> markerDates = MarkerDates(result);
        Assert.Equal(7, markerDates.Count);
        Assert.Equal(0, (int)result.warningsCount);
        Assert.Empty((IReadOnlyList<string>)result.orderWarnings);
    }

    [Fact]
    public async Task NoDatedPlans_ReturnsNotFound_AndNeverReads()
    {
        // Both candidates lack a parseable goal date, so nothing can be picked.
        var catalog = new FakeCatalog(
            Ref("A", "no date here", "x", null),
            Ref("B", "still no date", "y", null));
        var reader = new FakeReader(new());

        dynamic result = await PlanTools.GetCurrentPlan(catalog, reader, new FakeHolidaySource());

        Assert.False((bool)result.found);
        Assert.Equal(2, (int)result.candidateCount);
        Assert.Null(reader.LastId);
    }

    [Fact]
    public async Task PicksNearestFutureGoal_NotLatestOrFirst()
    {
        var catalog = new FakeCatalog(
            Ref("FAR", "far", "x", D(2026, 10, 31)),
            Ref("NEAR", "near", "x", D(2026, 9, 5)),
            Ref("PAST", "past", "x", D(2026, 8, 1)));
        var reader = new FakeReader(new()
        {
            ["FAR"] = InOrderPlan(),
            ["NEAR"] = InOrderPlan(),
            ["PAST"] = InOrderPlan(),
        });

        dynamic result = await PlanTools.GetCurrentPlan(
            catalog, reader, new FakeHolidaySource(), asOf: "2026-09-03");

        Assert.True((bool)result.found);
        Assert.Equal("NEAR", reader.LastId);
    }

    [Fact]
    public async Task NoFuturePlans_FallsBackToLatestPast()
    {
        var catalog = new FakeCatalog(
            Ref("OLD", "old", "x", D(2026, 6, 1)),
            Ref("RECENT", "recent", "x", D(2026, 8, 20)));
        var reader = new FakeReader(new()
        {
            ["OLD"] = InOrderPlan(),
            ["RECENT"] = InOrderPlan(),
        });

        dynamic result = await PlanTools.GetCurrentPlan(
            catalog, reader, new FakeHolidaySource(), asOf: "2026-09-03");

        Assert.True((bool)result.found);
        Assert.Equal("RECENT", reader.LastId);
    }

    [Fact]
    public async Task DefaultFilter_IsGfr()
    {
        var catalog = new FakeCatalog(Ref("A", "a", "x", D(2026, 9, 5)));
        var reader = new FakeReader(new() { ["A"] = InOrderPlan() });

        _ = await PlanTools.GetCurrentPlan(catalog, reader, new FakeHolidaySource(), asOf: "2026-09-03");

        Assert.Equal("[GFR]", catalog.LastFilter);
    }

    [Fact]
    public async Task CustomFilter_IsForwardedToCatalog()
    {
        var catalog = new FakeCatalog(Ref("A", "a", "x", D(2026, 9, 5)));
        var reader = new FakeReader(new() { ["A"] = InOrderPlan() });

        _ = await PlanTools.GetCurrentPlan(
            catalog, reader, new FakeHolidaySource(), filter: "Mariana", asOf: "2026-09-03");

        Assert.Equal("Mariana", catalog.LastFilter);
    }

    [Fact]
    public async Task AsOf_ShiftsSelectionForward()
    {
        var catalog = new FakeCatalog(
            Ref("SEP", "sep", "x", D(2026, 9, 5)),
            Ref("OCT", "oct", "x", D(2026, 10, 31)));
        var reader = new FakeReader(new()
        {
            ["SEP"] = InOrderPlan(),
            ["OCT"] = InOrderPlan(),
        });

        // As of Sep 6th the Sep 5th plan is already in the past, so October is current.
        dynamic result = await PlanTools.GetCurrentPlan(
            catalog, reader, new FakeHolidaySource(), asOf: "2026-09-06");

        Assert.Equal("OCT", reader.LastId);
        Assert.Equal("2026-09-06", (string)result.asOf);
    }

    [Fact]
    public async Task HolidayOnMarker_SurfacesWarning()
    {
        var catalog = new FakeCatalog(Ref("A", "a", "x", D(2026, 9, 5)));
        var reader = new FakeReader(new() { ["A"] = InOrderPlan() }); // QedDeploy = 2026-09-11
        var source = new FakeHolidaySource(
            new CountryHolidays("MX", null, [new Holiday(D(2026, 9, 11), "Test Holiday")]));

        dynamic result = await PlanTools.GetCurrentPlan(catalog, reader, source, asOf: "2026-09-03");

        Assert.Equal(1, (int)result.warningsCount);
        var summary = (IReadOnlyList<string>)result.warningsSummary;
        Assert.Contains("QedDeploy 2026-09-11 falls on a public holiday in MX", Assert.Single(summary));
    }

    [Fact]
    public async Task Markers_AreReturnedInChronologicalOrder()
    {
        // Reader hands back events scrambled; the tool must sort them by date.
        var scrambled = Plan(
            (Milestone.Release, D(2026, 9, 30)),
            (Milestone.StartDev, D(2026, 8, 3)),
            (Milestone.QedDeploy, D(2026, 9, 11)),
            (Milestone.EndDev, D(2026, 8, 28)));
        var catalog = new FakeCatalog(Ref("A", "a", "x", D(2026, 9, 5)));
        var reader = new FakeReader(new() { ["A"] = scrambled });

        dynamic result = await PlanTools.GetCurrentPlan(
            catalog, reader, new FakeHolidaySource(), asOf: "2026-09-03");

        List<string> dates = MarkerDates(result);
        var sorted = dates.OrderBy(d => d).ToList();
        Assert.Equal(sorted, dates);
    }

    [Fact]
    public async Task PlanIdAndTags_ComeFromTheReadPlan_WhenPresent()
    {
        var plan = InOrderPlan() with { PlanId = "PLAN-XYZ", Tags = ["2026.09_QED", "2026.10"] };
        var catalog = new FakeCatalog(Ref("A", "a", "x", D(2026, 9, 5)));
        var reader = new FakeReader(new() { ["A"] = plan });

        dynamic result = await PlanTools.GetCurrentPlan(
            catalog, reader, new FakeHolidaySource(), asOf: "2026-09-03");

        Assert.Equal("PLAN-XYZ", (string)result.planId);
        var tags = (IReadOnlyList<string>)result.tags;
        Assert.Contains("2026.10", tags);
    }
}
