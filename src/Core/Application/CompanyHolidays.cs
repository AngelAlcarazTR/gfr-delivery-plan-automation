namespace Core.Application;

// Generates the company holidays that affect delivery-plan scheduling for any year,
// derived from the recurring rules in the official HR calendars. This lets the engine
// cross-reference holidays for past and future years (2024, 2025, 2026, ...) without a
// per-year data file. Covers the USA federal + global "all countries" set, which are the
// Monday/weekday holidays that can shift the planned Start Development marker.
public static class CompanyHolidays
{
    public static IEnumerable<LocalDate> ForYear(int year)
    {
        yield return new LocalDate(year, 1, 1);                        // New Year's Day (global)
        yield return NthWeekday(year, 1, IsoDayOfWeek.Monday, 3);      // Martin Luther King Day
        yield return NthWeekday(year, 2, IsoDayOfWeek.Monday, 3);      // President's Day / Family Day
        yield return new LocalDate(year, 5, 8);                        // Global Mental Health Day
        yield return LastWeekday(year, 5, IsoDayOfWeek.Monday);        // Memorial Day
        yield return new LocalDate(year, 6, 19);                       // Juneteenth
        yield return new LocalDate(year, 7, 4);                        // Independence Day
        yield return NthWeekday(year, 9, IsoDayOfWeek.Monday, 1);      // Labor Day
        yield return new LocalDate(year, 10, 9);                       // Global Mental Health Day
        var thanksgiving = NthWeekday(year, 11, IsoDayOfWeek.Thursday, 4);
        yield return thanksgiving;                                     // Thanksgiving Day
        yield return thanksgiving.PlusDays(1);                         // Day After Thanksgiving
        yield return new LocalDate(year, 12, 25);                     // Christmas Day
    }

    // Convenience: a HolidayCalendar spanning the given years.
    public static HolidayCalendar Calendar(params int[] years)
        => new(years.SelectMany(ForYear));

    private static LocalDate NthWeekday(int year, int month, IsoDayOfWeek weekday, int n)
    {
        var date = new LocalDate(year, month, 1);
        var count = 0;
        while (true)
        {
            if (date.DayOfWeek == weekday && ++count == n)
                return date;
            date = date.PlusDays(1);
        }
    }

    private static LocalDate LastWeekday(int year, int month, IsoDayOfWeek weekday)
    {
        var date = new LocalDate(year, month, 1)
            .PlusMonths(1)
            .PlusDays(-1); // last day of month
        while (date.DayOfWeek != weekday)
            date = date.PlusDays(-1);
        return date;
    }
}
