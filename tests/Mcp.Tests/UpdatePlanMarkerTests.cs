using System.Collections;
using Core.Application;
using Core.Ports;
using static Mcp.Tests.PlanFactory;

namespace Mcp.Tests;

// Exercises the update_plan_marker tool over in-memory fakes for the ADO ports
// and the holiday source, so validation, the persist-then-re-read flow, marker
// mapping and holiday/order warnings are verified without touching the network.
public class UpdatePlanMarkerTests
{
    // --- fakes -------------------------------------------------------------

    private sealed class FakeWriter(MarkerUpdateResult result) : IDeliveryPlanWriter
    {
        public string? LastPlanId { get; private set; }
        public Milestone? LastMarker { get; private set; }
        public LocalDate? LastDate { get; private set; }
        public int Calls { get; private set; }

        public Task<PublishedPlanRef> CreateAsync(DeliveryPlan plan, PlanPublishOptions options, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task DeleteAsync(string planId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<MarkerUpdateResult> UpdateMarkerDateAsync(string planId, Milestone marker, LocalDate newDate, CancellationToken ct = default)
        {
            Calls++;
            LastPlanId = planId;
            LastMarker = marker;
            LastDate = newDate;
            return Task.FromResult(result);
        }
    }

    private sealed class FakeReader(DeliveryPlan plan) : IDeliveryPlanReader
    {
        public string? LastId { get; private set; }
        public Task<DeliveryPlan> GetPlanAsync(string planId, CancellationToken ct = default)
        {
            LastId = planId;
            return Task.FromResult(plan);
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

    private static DeliveryPlan NamedPlan(string name) =>
        InOrderPlan() with { Sprint = new Sprint(D(2026, 1, 1), name) };

    // --- tests -------------------------------------------------------------

    [Fact]
    public async Task UnknownMarker_Throws()
    {
        var writer = new FakeWriter(new MarkerUpdateResult(true, null, 1));
        var reader = new FakeReader(InOrderPlan());

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await PlanTools.UpdatePlanMarker(writer, reader, new FakeHolidaySource(),
                planId: "A", marker: "NotAMarker", date: "2026-09-15"));

        Assert.Equal(0, writer.Calls);
        Assert.Null(reader.LastId);
    }

    [Fact]
    public async Task EmptyPlanId_Throws()
    {
        var writer = new FakeWriter(new MarkerUpdateResult(true, null, 1));
        var reader = new FakeReader(InOrderPlan());

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await PlanTools.UpdatePlanMarker(writer, reader, new FakeHolidaySource(),
                planId: "  ", marker: "QedDeploy", date: "2026-09-15"));

        Assert.Equal(0, writer.Calls);
    }

    [Fact]
    public async Task MarkerNotFound_ReturnsUpdatedFalse_AndNeverReads()
    {
        var writer = new FakeWriter(new MarkerUpdateResult(Found: false, PreviousDate: null, UpdatedCount: 0));
        var reader = new FakeReader(InOrderPlan());

        dynamic result = await PlanTools.UpdatePlanMarker(
            writer, reader, new FakeHolidaySource(),
            planId: "A", marker: "Release", date: "2026-09-30");

        Assert.False((bool)result.updated);
        Assert.Equal("A", (string)result.planId);
        Assert.Equal("Release", (string)result.marker);
        Assert.Equal(1, writer.Calls);
        Assert.Null(reader.LastId);              // no re-read when nothing changed
    }

    [Fact]
    public async Task Success_PersistsThenReReads_AndReportsDates()
    {
        var writer = new FakeWriter(new MarkerUpdateResult(Found: true, PreviousDate: D(2026, 9, 11), UpdatedCount: 1));
        var reader = new FakeReader(NamedPlan("[GFR][2026][Delivery Plan] - September 11th QED Release"));

        dynamic result = await PlanTools.UpdatePlanMarker(
            writer, reader, new FakeHolidaySource(),
            planId: "A", marker: "qeddeploy", date: "2026-09-15");   // case-insensitive marker

        Assert.True((bool)result.updated);
        Assert.Equal("A", (string)result.planId);
        Assert.Equal("[GFR][2026][Delivery Plan] - September 11th QED Release", (string)result.planName);
        Assert.Equal("QedDeploy", (string)result.marker);
        Assert.Equal("2026-09-11", (string)result.previousDate);
        Assert.Equal("2026-09-15", (string)result.newDate);

        // Writer received the parsed marker + date; reader re-read the same plan.
        Assert.Equal(Milestone.QedDeploy, writer.LastMarker);
        Assert.Equal(D(2026, 9, 15), writer.LastDate);
        Assert.Equal("A", reader.LastId);

        List<string> markerDates = MarkerDates(result);
        Assert.Equal(7, markerDates.Count);
        Assert.Equal(0, (int)result.warningsCount);
        Assert.Empty((IReadOnlyList<string>)result.orderWarnings);
    }

    [Fact]
    public async Task Success_SurfacesHolidayWarning_OnReReadPlan()
    {
        // Re-read plan has QED on 2026-09-11; make that a public holiday in IN-KA.
        var writer = new FakeWriter(new MarkerUpdateResult(Found: true, PreviousDate: D(2026, 9, 4), UpdatedCount: 1));
        var reader = new FakeReader(InOrderPlan());
        var holidays = new FakeHolidaySource(
            new CountryHolidays("IN", "KA", [new Holiday(D(2026, 9, 11), "Ganesh Chaturthi")]));

        dynamic result = await PlanTools.UpdatePlanMarker(
            writer, reader, holidays,
            planId: "A", marker: "QedDeploy", date: "2026-09-11");

        Assert.True((bool)result.updated);
        Assert.True((int)result.warningsCount >= 1);
        Assert.Contains((IReadOnlyList<string>)result.warningsSummary,
            s => s.Contains("Ganesh Chaturthi"));
    }
}
