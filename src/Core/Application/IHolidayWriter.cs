namespace Core.Application;

/// <summary>
/// Defines a writer for exporting holiday data to various formats or destinations.
/// </summary>
public interface IHolidayWriter
{
    Task WriteAsync(int year, IReadOnlyList<Holiday> holidays, CancellationToken cancellationToken = default);

    Task WriteAsync(int year, string? country, string region, IReadOnlyList<Holiday> holidays, CancellationToken cancellationToken = default);
}