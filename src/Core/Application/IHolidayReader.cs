namespace Core.Application;

// Port: supplies the company holidays for a given year, from whatever backing
// store implements it (a Blob today, potentially something else tomorrow).
// Lives in Core with ZERO references to Azure/Blob — the adapter does the I/O.
//
// Returns null when the year is not available in the store, so the caller can
// fall back (e.g. to CompanyHolidays) instead of failing.
public interface IHolidayReader
{
    Task<IReadOnlyList<LocalDate>?> GetHolidaysAsync(int year, CancellationToken cancellationToken = default);
}