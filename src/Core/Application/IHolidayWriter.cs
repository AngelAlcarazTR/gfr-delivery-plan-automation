namespace Core.Application;

public interface IHolidayWriter
{
    Task WriteAsync(int year, IReadOnlyList<Holiday> holidays, CancellationToken cancellationToken = default);
}
