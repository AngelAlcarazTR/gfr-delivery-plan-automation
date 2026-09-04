using System.Collections;
using Core.Application;
using Core.Ports;
using static Mcp.Tests.PlanFactory;

namespace Mcp.Tests;

// Exercises the auto_resolve_holidays tool and its NextClearBusinessDay helper over
// in-memory fakes, so the conflict scan, forward roll (weekend + multi-calendar aware),
// dry-run vs apply and the persist-then-re-read flow are verified without the network.
public class AutoResolveHolidaysTests
{
    // --- fakes -------------------------------------------------------------

    // Returns a different plan on each successive call (detect, then re-read).
    private sealed class SequenceReader(params DeliveryPlan[] plans) : IDeliveryPlanReader
    {
        private int _i;
        public int Calls => _i;
        public Task<DeliveryPlan> GetPlanAsync(string planId, CancellationToken ct = default)
        {
            var p = plans[Math.Min(_i, plans.Length - 1)];
            _i++;
            return Task.FromResult(p);
        }
    }

    private sealed class RecordingWriter : IDeliveryPlanWriter
    {
        public List<(Milestone Marker, LocalDate Date)> Updates { get; } = new();

        public Task<PublishedPlanRef> CreateAsync(DeliveryPlan plan, PlanPublishOptions options, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task DeleteAsync(string planId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<MarkerUpdateResult> UpdateMarkerDateAsync(string planId, Milestone marker, LocalDate newDate, CancellationToken ct = default)
        {
            Updates.Add((marker, newDate));
            return Task.FromResult(new MarkerUpdateResult(Found: true, PreviousDate: null, UpdatedCount: 1));
        }
    }

    private sealed class FakeHolidaySource(params CountryHolidays[] calendars) : IHolidayCalendarSource
    {
        public Task<IReadOnlyList<CountryHolidays>> GetCalendarAsync(IEnumerable<int> years, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CountryHolidays>>(calendars);
    }

    private static CountryHolidays In(string region, params LocalDate[] dates) =>
        new("IN", region, dates.Select(d => new Holiday(d, "Test Holiday")));

    // --- helpers -----------------------------------------------------------

    private static IReadOnlyList<object> Resolutions(dynamic result)
    {
        var list = new List<object>();
        foreach (var r in (IEnumerable)result.resolutions)
            list.Add(r);
        return list;
    }

    // --- NextClearBusinessDay unit tests -----------------------------------

    [Fact]
    public void NextClearBusinessDay_AlreadyClear_ReturnsInput()
    {
        // 2026-09-11 is a Friday and no holiday -> already a business day.
        var got = PlanTools.NextClearBusinessDay(D(2026, 9, 11), new List<CountryHolidays>());
        Assert.Equal(D(2026, 9, 11), got);
    }

    [Fact]
    public void NextClearBusinessDay_HolidayOnThursday_RollsToFriday()
    {
        var cals = new List<CountryHolidays> { In("KA", D(2026, 9, 10)) };   // Thursday holiday
        var got = PlanTools.NextClearBusinessDay(D(2026, 9, 10), cals);
        Assert.Equal(D(2026, 9, 11), got);
    }

    [Fact]
    public void NextClearBusinessDay_HolidayOnFriday_SkipsWeekendToMonday()
    {
        var cals = new List<CountryHolidays> { In("KA", D(2026, 9, 11)) };   // Friday holiday
        var got = PlanTools.NextClearBusinessDay(D(2026, 9, 11), cals);
        Assert.Equal(D(2026, 9, 14), got);                                    // Monday
    }

    [Fact]
    public void NextClearBusinessDay_ConsecutiveHolidaysAcrossCalendars_SkipsAll()
    {
        // Thu holiday in KA, Fri holiday in TG -> must land on Monday.
        var cals = new List<CountryHolidays>
        {
            In("KA", D(2026, 9, 10)),
            In("TG", D(2026, 9, 11)),
        };
        var got = PlanTools.NextClearBusinessDay(D(2026, 9, 10), cals);
        Assert.Equal(D(2026, 9, 14), got);
    }

    // --- tool tests --------------------------------------------------------

    [Fact]
    public async Task EmptyPlanId_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await PlanTools.AutoResolveHolidays(
                new SequenceReader(InOrderPlan()), new RecordingWriter(), new FakeHolidaySource(),
                planId: "  "));
    }

    [Fact]
    public async Task NoConflicts_ReturnsNothingToResolve_AndWritesNothing()
    {
        var reader = new SequenceReader(InOrderPlan());
        var writer = new RecordingWriter();

        dynamic result = await PlanTools.AutoResolveHolidays(
            reader, writer, new FakeHolidaySource(), planId: "A");   // no holidays at all

        Assert.False((bool)result.applied);
        Assert.Equal(0, (int)result.conflictCount);
        Assert.Empty(writer.Updates);
        Assert.Equal(1, reader.Calls);                               // detect only, no re-read
    }

    [Fact]
    public async Task DryRun_ProposesForwardMove_WritesNothing()
    {
        // QaCutoff sits on Thursday 2026-09-10, a holiday in IN-KA.
        var plan = Plan(
            (Milestone.QaCutoff, D(2026, 9, 10)),
            (Milestone.Release, D(2026, 9, 30)));
        var reader = new SequenceReader(plan);
        var writer = new RecordingWriter();
        var holidays = new FakeHolidaySource(In("KA", D(2026, 9, 10)));

        dynamic result = await PlanTools.AutoResolveHolidays(
            reader, writer, holidays, planId: "A", dryRun: true);

        Assert.False((bool)result.applied);
        Assert.True((bool)result.dryRun);
        Assert.Equal(1, (int)result.conflictCount);

        var all = Resolutions(result);
        Assert.Single(all);
        dynamic res = all[0];
        Assert.Equal("QaCutoff", (string)res.marker);
        Assert.Equal("2026-09-10", (string)res.from);
        Assert.Equal("2026-09-11", (string)res.to);      // Thu -> Fri
        Assert.Equal(1, (int)res.daysShifted);

        Assert.Empty(writer.Updates);                    // preview writes nothing
        Assert.Equal(1, reader.Calls);
    }

    [Fact]
    public async Task Apply_MovesMarkerOffHoliday_SkipsWeekend_AndReReads()
    {
        // Detect sees QedDeploy on Friday 2026-09-11 (holiday); resolved plan has it on Monday.
        var conflicted = Plan(
            (Milestone.QedDeploy, D(2026, 9, 11)),
            (Milestone.Release, D(2026, 9, 30)));
        var resolved = Plan(
            (Milestone.QedDeploy, D(2026, 9, 14)),
            (Milestone.Release, D(2026, 9, 30)));
        var reader = new SequenceReader(conflicted, resolved);
        var writer = new RecordingWriter();
        var holidays = new FakeHolidaySource(In("KA", D(2026, 9, 11)));

        dynamic result = await PlanTools.AutoResolveHolidays(
            reader, writer, holidays, planId: "A", dryRun: false);

        Assert.True((bool)result.applied);
        Assert.Equal(1, (int)result.resolvedCount);

        // Writer received a single move: QedDeploy -> Monday (weekend skipped).
        Assert.Single(writer.Updates);
        Assert.Equal((Milestone.QedDeploy, D(2026, 9, 14)), writer.Updates[0]);

        // Re-read happened and shows the clean plan with no remaining warnings.
        Assert.Equal(2, reader.Calls);
        Assert.Equal(0, (int)result.remainingWarningsCount);

        var moved = Resolutions(result);
        Assert.Single(moved);
        dynamic m = moved[0];
        Assert.Equal("2026-09-11", (string)m.from);
        Assert.Equal("2026-09-14", (string)m.to);
        Assert.Equal(3, (int)m.daysShifted);
    }

    [Fact]
    public async Task Apply_ResolvesMultipleMarkers_InEnumOrder()
    {
        // Two markers on the same Thursday holiday.
        var conflicted = Plan(
            (Milestone.EndDev, D(2026, 9, 10)),
            (Milestone.QaCutoff, D(2026, 9, 10)));
        var resolved = Plan(
            (Milestone.EndDev, D(2026, 9, 11)),
            (Milestone.QaCutoff, D(2026, 9, 11)));
        var reader = new SequenceReader(conflicted, resolved);
        var writer = new RecordingWriter();
        var holidays = new FakeHolidaySource(In("KA", D(2026, 9, 10)));

        dynamic result = await PlanTools.AutoResolveHolidays(
            reader, writer, holidays, planId: "A", dryRun: false);

        Assert.Equal(2, writer.Updates.Count);
        Assert.Equal(Milestone.EndDev, writer.Updates[0].Marker);     // enum order: EndDev before QaCutoff
        Assert.Equal(Milestone.QaCutoff, writer.Updates[1].Marker);
        Assert.All(writer.Updates, u => Assert.Equal(D(2026, 9, 11), u.Date));
    }
}
