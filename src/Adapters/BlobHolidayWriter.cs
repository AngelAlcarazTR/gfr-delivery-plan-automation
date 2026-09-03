namespace Adapters;

/// <summary>
/// Writes holiday data to Azure Blob Storage in JSON format.
/// </summary>
/// <param name="options">The options for configuring the BlobHolidayWriter.</param>
public sealed class BlobHolidayWriter(BlobHolidayReaderOptions options) : IHolidayWriter
{
    private readonly BlobHolidayReaderOptions _opt = options;
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>
    /// Writes holiday data to Azure Blob Storage in JSON format for a specific year.
    /// </summary>
    /// <param name="year">The year for which to write holiday data.</param>
    /// <param name="holidays">The list of holidays to write.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous write operation.</returns>
    public Task WriteAsync(int year, IReadOnlyList<Holiday> holidays,
        CancellationToken cancellationToken = default)
        => WriteAsync(year, null, null, holidays, cancellationToken);

    /// <summary>
    /// Writes holiday data to Azure Blob Storage in JSON format for a specific year, country, and region.
    /// </summary>
    /// <param name="year">The year for which to write holiday data.</param>
    /// <param name="country">The country for which to write holiday data.</param>
    /// <param name="region">The region within the country for which to write holiday data.</param>
    /// <param name="holidays">The list of holidays to write.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous write operation.</returns>
    public async Task WriteAsync(int year, string? country, string? region,
        IReadOnlyList<Holiday> holidays, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_opt.ConnectionString))
            throw new InvalidOperationException(
                "Cannot write holidays: the 'HolidaysStorage' connection string is not configured. " +
                "Set it in user-secrets (Development) or app settings/environment variables (Production).");

        var container = new BlobContainerClient(_opt.ConnectionString, _opt.ContainerName);
        await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var dto = new HolidayFileDto(
            year, country, region,
            [.. holidays.Select(h => new HolidayEntry(Iso(h.Date), h.Name))]);

        var blob = container.GetBlobClient(BlobName(year, country, region));
        await blob.UploadAsync(BinaryData.FromObjectAsJson(dto, Json),
            overwrite: true, cancellationToken);
    }

    private static string BlobName(int year, string? country, string? region)
    {
        if (string.IsNullOrWhiteSpace(country)) return $"holidays-{year}.json";
        return string.IsNullOrWhiteSpace(region)
            ? $"holidays-{year}-{country}.json"
            : $"holidays-{year}-{country}-{region}.json";
    }

    private static string Iso(LocalDate d) => $"{d.Year:D4}-{d.Month:D2}-{d.Day:D2}";

    private sealed record HolidayFileDto(
        int Year, string? Country, string? Region, IReadOnlyList<HolidayEntry> Holidays);
    private sealed record HolidayEntry(string Date, string Name);
}