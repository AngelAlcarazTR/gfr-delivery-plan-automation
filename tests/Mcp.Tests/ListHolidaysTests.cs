using System.Collections;
using Core.Application;

namespace Mcp.Tests;

// Exercises the list_holidays tool over an in-memory holiday source, so the
// read-only projection, ordering by date and the country/region filters are
// verified without hitting Blob Storage. No plan is involved.
public class ListHolidaysTests
{
    // --- fakes -------------------------------------------------------------

    // Records the years requested and returns the configured calendars as-is.
    private sealed class FakeHolidaySource(params CountryHolidays[] calendars) : IHolidayCalendarSource
    {
        public List<int> RequestedYears { get; } = new();

        public Task<IReadOnlyList<CountryHolidays>> GetCalendarAsync(IEnumerable<int> years, CancellationToken ct = default)
        {
            RequestedYears.AddRange(years);
            return Task.FromResult<IReadOnlyList<CountryHolidays>>(calendars);
        }
    }

    private static CountryHolidays Cal(string country, string? region, params (int m, int d, string name)[] holidays) =>
        new(country, region, holidays.Select(h => new Holiday(new LocalDate(2026, h.m, h.d), h.name)));

    // --- helpers -----------------------------------------------------------

    private static IReadOnlyList<object> Holidays(dynamic result)
    {
        var list = new List<object>();
        foreach (var h in (IEnumerable)result.holidays)
            list.Add(h);
        return list;
    }

    // --- tests -------------------------------------------------------------

    [Fact]
    public async Task ListsAllHolidays_OrderedByDate()
    {
        var source = new FakeHolidaySource(
            Cal("MX", null, (12, 25, "Christmas"), (1, 1, "New Year")),
            Cal("US", null, (7, 4, "Independence Day")));

        dynamic result = await PlanTools.ListHolidays(source, year: 2026);

        Assert.Equal(2026, (int)result.year);
        Assert.Equal(3, (int)result.count);

        var all = Holidays(result);
        Assert.Equal(3, all.Count);
        Assert.Equal("2026-01-01", (string)((dynamic)all[0]).date);
        Assert.Equal("2026-07-04", (string)((dynamic)all[1]).date);
        Assert.Equal("2026-12-25", (string)((dynamic)all[2]).date);
    }

    [Fact]
    public async Task ProjectsDateNameCountryRegion()
    {
        var source = new FakeHolidaySource(Cal("IN", "KA", (8, 15, "Independence Day")));

        dynamic result = await PlanTools.ListHolidays(source, year: 2026);

        var one = (dynamic)Holidays(result)[0];
        Assert.Equal("2026-08-15", (string)one.date);
        Assert.Equal("Independence Day", (string)one.name);
        Assert.Equal("IN", (string)one.country);
        Assert.Equal("KA", (string)one.region);
    }

    [Fact]
    public async Task FiltersByCountry()
    {
        var source = new FakeHolidaySource(
            Cal("MX", null, (11, 20, "Revolution Day")),
            Cal("US", null, (11, 26, "Thanksgiving")));

        dynamic result = await PlanTools.ListHolidays(source, year: 2026, country: "MX");

        Assert.Equal(1, (int)result.count);
        var one = (dynamic)Holidays(result)[0];
        Assert.Equal("MX", (string)one.country);
        Assert.Equal("2026-11-20", (string)one.date);
    }

    [Fact]
    public async Task FiltersByCountry_IsCaseInsensitive()
    {
        var source = new FakeHolidaySource(Cal("MX", null, (11, 20, "Revolution Day")));

        dynamic result = await PlanTools.ListHolidays(source, year: 2026, country: "mx");

        Assert.Equal(1, (int)result.count);
    }

    [Fact]
    public async Task FiltersByRegion()
    {
        var source = new FakeHolidaySource(
            Cal("IN", "KA", (11, 1, "Kannada Rajyotsava")),
            Cal("IN", "TG", (10, 6, "Bathukamma")));

        dynamic result = await PlanTools.ListHolidays(source, year: 2026, region: "TG");

        Assert.Equal(1, (int)result.count);
        var one = (dynamic)Holidays(result)[0];
        Assert.Equal("TG", (string)one.region);
        Assert.Equal("2026-10-06", (string)one.date);
    }

    [Fact]
    public async Task NoHolidays_ReturnsCountZero()
    {
        var source = new FakeHolidaySource();

        dynamic result = await PlanTools.ListHolidays(source, year: 2026);

        Assert.Equal(0, (int)result.count);
        Assert.Empty(Holidays(result));
    }

    [Fact]
    public async Task PassesRequestedYearToSource()
    {
        var source = new FakeHolidaySource();

        await PlanTools.ListHolidays(source, year: 2027);

        Assert.Equal(new[] { 2027 }, source.RequestedYears);
    }
}
