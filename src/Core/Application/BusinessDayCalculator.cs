namespace Core.Application;

public static class BusinessDayCalculator
{
    // Adds (or, when businessDays is negative, subtracts) whole business days,
    // skipping weekends. Positive counts walk forward, negative counts walk back.
    public static LocalDate AddBusinessDays(LocalDate start, int businessDays)
    {
        if (businessDays == 0)
            return start;

        var step = businessDays > 0 ? 1 : -1;
        var remaining = Math.Abs(businessDays);
        var date = start;

        while (remaining > 0)
        {
            date = date.PlusDays(step);

            if (date.DayOfWeek != IsoDayOfWeek.Saturday &&
                date.DayOfWeek != IsoDayOfWeek.Sunday)
            {
                remaining--;
            }
        }

        return date;
    }

    // Count of business days strictly between two dates (order-independent, weekends excluded).
    public static int BusinessDaysBetween(LocalDate a, LocalDate b)
    {
        var lo = a < b ? a : b;
        var hi = a < b ? b : a;
        var count = 0;
        var date = lo;

        while (date < hi)
        {
            date = date.PlusDays(1);
            if (date.DayOfWeek != IsoDayOfWeek.Saturday &&
                date.DayOfWeek != IsoDayOfWeek.Sunday)
            {
                count++;
            }
        }

        return count;
    }
}