namespace Adapters;

public sealed class BlobHolidayWriter(BlobHolidayReaderOptions options) : IHolidayWriter
{
    private readonly BlobHolidayReaderOptions _opt = options;
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task WriteAsync(int year, IReadOnlyList<Holiday> holidays,
        CancellationToken cancellationToken = default)
    {
        var container = new BlobContainerClient(_opt.ConnectionString, _opt.ContainerName);
        await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var dto = new HolidayFileDto(
            year,
            [.. holidays.Select(h => new HolidayEntry(Iso(h.Date), h.Name))]);

        var blob = container.GetBlobClient($"holidays-{year}.json");
        await blob.UploadAsync(BinaryData.FromObjectAsJson(dto, Json),
            overwrite: true, cancellationToken);
    }

    private static string Iso(LocalDate d) => $"{d.Year:D4}-{d.Month:D2}-{d.Day:D2}";

    private sealed record HolidayFileDto(int Year, IReadOnlyList<HolidayEntry> Holidays);
    private sealed record HolidayEntry(string Date, string Name);
}