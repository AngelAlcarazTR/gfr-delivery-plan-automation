namespace Adapters;

public class AdoSprintReader(HttpClient http, AdoConfig config) : ISprintReader
{
    private readonly HttpClient _http = http;
    private readonly AdoConfig _config = config;

    public async Task<Sprint> GetCurrentSprintAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _config.CurrentIterationsUrl());
        request.Headers.Authorization = _config.AuthHeader();

        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        return ParseCurrentSprint(json);
    }

    public static Sprint ParseCurrentSprint(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var iteration = root.GetProperty("value")[0];
        var name = iteration.GetProperty("name").GetString()!;
        var startDateRaw = iteration
            .GetProperty("attributes")
            .GetProperty("startDate")
            .GetString()!;

        var startDate = ParseAdoDate(startDateRaw);

        return new Sprint(startDate, name);
    }

    private static LocalDate ParseAdoDate(string raw)
    {
        // ADO manda "2026-07-29T00:00:00Z" — tomamos solo la fecha
        var instant = InstantPattern.General.Parse(raw).Value;
        return instant.InUtc().Date;
    }
}
