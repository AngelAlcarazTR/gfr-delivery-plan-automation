namespace Adapters;

// Adapter: reads the holiday calendar for a year from Blob Storage.
// Blob layout: container "holidays", blob "holidays-{year}.json" with shape:
//   { "year": 2026, "holidays": ["2026-01-01", "2026-07-03", ... ] }
//
// The blob is filled MANUALLY (once a year) by the loader that reads the official
// SharePoint page. This reader only consumes it — it never touches SharePoint,
// so the engine has no live dependency on Graph.
//
// Returns null when the year's blob does not exist, so the caller can fall back
// to CompanyHolidays instead of failing.
public sealed class BlobHolidayReader(BlobHolidayReaderOptions options) : IHolidayReader
{
    private readonly BlobHolidayReaderOptions _opt = options;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<LocalDate>?> GetHolidaysAsync(
        int year, CancellationToken cancellationToken = default)
    {
        var container = new BlobContainerClient(_opt.ConnectionString, _opt.ContainerName);
        var blob = container.GetBlobClient(BlobName(year));

        try
        {
            var response = await blob.DownloadContentAsync(cancellationToken);
            var dto = response.Value.Content.ToObjectFromJson<HolidayFileDto>(Json);

            if (dto?.Holidays is null || dto.Holidays.Count == 0)
                return null;

            // Parse each "yyyy-MM-dd" into a LocalDate; skip anything malformed.
            var dates = new List<LocalDate>();
            foreach (var el in dto.Holidays)
            {
                var s = el.ValueKind == JsonValueKind.Object
                    ? el.GetProperty("date").GetString()
                    : el.GetString();
                if (s is not null && TryParseIso(s, out var d))
                    dates.Add(d);
            }

            return dates.Count == 0 ? null : dates;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Year not loaded yet -> let the caller fall back.
            return null;
        }
    }

    private static string BlobName(int year) => $"holidays-{year}.json";

    private static bool TryParseIso(string s, out LocalDate date)
    {
        date = default;
        if (DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dt))
        {
            date = new LocalDate(dt.Year, dt.Month, dt.Day);
            return true;
        }
        return false;
    }

    // The JSON shape stored in the blob.
    private sealed record HolidayFileDto(int Year, IReadOnlyList<JsonElement> Holidays);
}

// Config for the reader. ConnectionString comes from app settings
// (e.g. "HolidaysStorage"), NOT hardcoded — same principle as AdoConfig.
public sealed record BlobHolidayReaderOptions(
    string ConnectionString,
    string ContainerName = "holidays");