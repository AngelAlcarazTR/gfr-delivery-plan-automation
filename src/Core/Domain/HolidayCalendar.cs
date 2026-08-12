namespace Core.Domain;

// An immutable set of non-working days used to roll planning markers off holidays.
// Weekends are always non-working; the supplied dates add company holidays on top.
public sealed class HolidayCalendar
{
    public static readonly HolidayCalendar None = new(Array.Empty<LocalDate>());

    private readonly HashSet<LocalDate> _holidays;

    public HolidayCalendar(IEnumerable<LocalDate> holidays)
        => _holidays = new HashSet<LocalDate>(holidays);

    public bool IsHoliday(LocalDate date) => _holidays.Contains(date);

    public bool IsBusinessDay(LocalDate date) =>
        date.DayOfWeek != IsoDayOfWeek.Saturday &&
        date.DayOfWeek != IsoDayOfWeek.Sunday &&
        !_holidays.Contains(date);

    // Moves forward to the next weekday that is not a holiday (returns the input if already one).
    public LocalDate RollForwardToBusinessDay(LocalDate date)
    {
        var d = date;
        while (!IsBusinessDay(d))
        {
            d = d.PlusDays(1);
        }
        return d;
    }
}
