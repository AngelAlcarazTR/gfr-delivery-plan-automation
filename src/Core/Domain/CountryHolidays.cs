namespace Core.Domain;

/// <summary>
/// Represents a collection of holidays for a specific country and optional region.
/// </summary>
public sealed class CountryHolidays
{
    private readonly IReadOnlyDictionary<LocalDate, string> _byDate;

    public string? Country { get; } = null;
    public string? Region { get; } = null;

    /// <summary>
    /// Initializes a new instance of the <see cref="CountryHolidays"/> class with the specified country, region, and holidays.
    /// </summary>
    /// <param name="country">The country for which the holidays are defined.</param>
    /// <param name="region">The optional region within the country for which the holidays are defined.</param>
    /// <param name="holidays">A collection of holidays.</param>
    public CountryHolidays(string country, string? region, IEnumerable<Holiday> holidays)
    {
        Country = country;
        Region = region;

        var map = new Dictionary<LocalDate, string>();
        foreach (var h in holidays)
            map[h.Date] = h.Name;
        _byDate = map;
    }

    /// <summary>
    /// Tries to get the holiday name for the specified date.
    /// </summary>
    /// <param name="date">The date for which to get the holiday name.</param>
    /// <param name="holidayName">When this method returns, contains the holiday name if the date is a holiday; otherwise, null.</param>
    /// <returns>true if the date is a holiday; otherwise, false.</returns>
    public bool TryGet(LocalDate date, out string holidayName)
        => _byDate.TryGetValue(date, out holidayName!);
}
