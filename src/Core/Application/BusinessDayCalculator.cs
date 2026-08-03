namespace Core.Application;

public static class BusinessDayCalculator
{
    public static LocalDate AddBusinessDays(LocalDate start, int businessDays)
    {
        var date = start;
        var added = 0;

        while (added < businessDays)
        {
            date = date.PlusDays(1);

            if (date.DayOfWeek != IsoDayOfWeek.Saturday &&
                date.DayOfWeek != IsoDayOfWeek.Sunday)
            {
                added++;
            }
        }

        return date;
    }
}