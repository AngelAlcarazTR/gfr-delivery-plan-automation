namespace Core.Application;

/// <summary>
/// Defines a source for retrieving holiday calendars for specified years.
/// </summary>
public interface IHolidayCalendarSource
{
    /// <summary>
    /// Gets the holiday calendars for the specified years.
    /// </summary>
    /// <param name="years">The years for which to retrieve holiday calendars.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of country holiday calendars.</returns>
    Task<IReadOnlyList<CountryHolidays>> GetCalendarAsync(IEnumerable<int> years, CancellationToken cancellationToken = default);
}