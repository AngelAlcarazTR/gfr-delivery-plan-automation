using Azure.Storage.Blobs.Models;

namespace Adapters;

public sealed class BlobHolidayCalendarSource(BlobHolidayReaderOptions options) : IHolidayCalendarSource
{
    private readonly BlobHolidayReaderOptions _opt = options;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<CountryHolidays>> GetCalendarAsync(
        IEnumerable<int> years, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_opt.ConnectionString))
            return [];

        var container = new BlobContainerClient(_opt.ConnectionString, _opt.ContainerName);
        var grouped = new Dictionary<(string Country, string? Region), List<Holiday>>();

        foreach (var year in years.Distinct())
        {
            var prefix = $"holidays-{year}-";
            await foreach (var item in container.GetBlobsAsync(
                                BlobTraits.None, BlobStates.None, prefix, cancellationToken))
            {
                CountryHolidayFileDto? dto;
                try
                {
                    var blob = container.GetBlobClient(item.Name);
                    var response = await blob.DownloadContentAsync(cancellationToken);
                    dto = response.Value.Content.ToObjectFromJson<CountryHolidayFileDto>(Json);
                }
                catch (RequestFailedException)
                {
                    continue;
                }

                if (dto?.Holidays is null || string.IsNullOrWhiteSpace(dto.Country))
                    continue;

                var key = (dto.Country!, dto.Region);
                if (!grouped.TryGetValue(key, out var list))
                    grouped[key] = list = [];

                foreach (var h in dto.Holidays)
                    if (h.Date is not null && h.Name is not null && TryParseIso(h.Date, out var d))
                        list.Add(new Holiday(d, h.Name));
            }
        }

        return [.. grouped.Select(kv => new CountryHolidays(kv.Key.Country, kv.Key.Region, kv.Value))];
    }

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

    private sealed record CountryHolidayFileDto(
        int Year, string? Country, string? Region, IReadOnlyList<HolidayEntryDto>? Holidays);

    private sealed record HolidayEntryDto(string? Date, string? Name);
}