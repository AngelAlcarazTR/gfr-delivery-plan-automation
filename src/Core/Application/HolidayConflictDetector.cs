namespace Core.Application;

/// <summary>
/// Detects conflicts between planned events and holidays in specified country calendars.
/// </summary>
public static class HolidayConflictDetector
{
    /// <summary>
    /// Detects conflicts between planned events and holidays in specified country calendars.
    /// </summary>
    /// <param name="events">The list of planned events to check for conflicts.</param>
    /// <param name="calendars">The list of country holiday calendars to check against.</param>
    /// <returns>A list of detected holiday conflicts.</returns>
    public static IReadOnlyList<HolidayConflict> Detect(IReadOnlyList<PlanEvent> events, IReadOnlyList<CountryHolidays> calendars)
    {
        var conflicts = new List<HolidayConflict>();
        if (events is null || calendars is null)
            return conflicts;

        foreach (var e in events)
            foreach (var cal in calendars)
                if (cal.TryGet(e.Date, out var name))
                    conflicts.Add(new HolidayConflict(
        e.Label, e.Date, cal.Country ?? string.Empty, cal.Region, name));

        return conflicts;
    }
}