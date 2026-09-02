namespace Core.Tests;

public class HolidayConflictDetectorTests
{
    private static PlanEvent Ev(Milestone m, int y, int mo, int d)
        => new(m, new LocalDate(y, mo, d), false, null);

    private static CountryHolidays Cal(string country, string? region, params (int y, int mo, int d, string name)[] days)
        => new(country, region, days.Select(x => new Holiday(new LocalDate(x.y, x.mo, x.d), x.name)));

    [Fact]
    public void Flags_a_marker_that_lands_on_an_india_holiday()
    {
        var events = new[] { Ev(Milestone.QedDeploy, 2026, 9, 14) };
        var calendars = new[] { Cal("IN", "MH", (2026, 9, 14, "Ganesh Chaturthi")) };

        var conflicts = HolidayConflictDetector.Detect(events, calendars);

        var c = Assert.Single(conflicts);
        Assert.Equal(Milestone.QedDeploy, c.Marker);
        Assert.Equal("IN", c.Country);
        Assert.Equal("MH", c.Region);
        Assert.Equal("Ganesh Chaturthi", c.HolidayName);
    }

    [Fact]
    public void No_conflicts_when_no_marker_lands_on_a_holiday()
    {
        var events = new[] { Ev(Milestone.QedDeploy, 2026, 9, 14) };
        var calendars = new[] { Cal("MX", null, (2026, 9, 16, "Independence of Mexico")) };

        var conflicts = HolidayConflictDetector.Detect(events, calendars);

        Assert.Empty(conflicts);
    }

    [Fact]
    public void Flags_the_same_date_across_multiple_countries()
    {
        var events = new[] { Ev(Milestone.StartReg, 2026, 9, 16) };
        var calendars = new[]
        {
            Cal("MX", null, (2026, 9, 16, "Independence of Mexico")),
            Cal("US", null, (2026, 9, 16, "Some US Day")),
        };

        var conflicts = HolidayConflictDetector.Detect(events, calendars);

        Assert.Equal(2, conflicts.Count);
        Assert.Contains(conflicts, x => x.Country == "MX");
        Assert.Contains(conflicts, x => x.Country == "US");
    }

    [Fact]
    public void Detects_conflict_in_the_prior_year_for_busy_season_startdev()
    {
        var events = new[]
        {
            Ev(Milestone.StartDev, 2026, 12, 21),
            Ev(Milestone.QedDeploy, 2027, 1, 18),
        };
        var calendars = new[] { Cal("MX", null, (2026, 12, 21, "Company Shutdown")) };

        var conflicts = HolidayConflictDetector.Detect(events, calendars);

        var c = Assert.Single(conflicts);
        Assert.Equal(Milestone.StartDev, c.Marker);
        Assert.Equal(2026, c.Date.Year);
    }

    [Fact]
    public void Empty_inputs_yield_no_conflicts()
    {
        Assert.Empty(HolidayConflictDetector.Detect(
            System.Array.Empty<PlanEvent>(), System.Array.Empty<CountryHolidays>()));
    }
}