namespace Adapters;

public sealed class SharePointHolidayLoader(
    GraphConfig graphConfig,
    SharePointHolidaySource source,
    BlobHolidayReaderOptions blob)
{
    private readonly GraphConfig _graph = graphConfig;
    private readonly SharePointHolidaySource _source = source;
    private readonly BlobHolidayReaderOptions _blob = blob;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<LocalDate>> LoadAsync(int year, CancellationToken ct = default)
    {
        var innerHtml = await FetchPageInnerHtmlAsync(ct);
        if (string.IsNullOrWhiteSpace(innerHtml))
            throw new InvalidOperationException("Could not find the holiday table (textWebPart innerHtml) on the page.");

        var holidays = HolidayHtmlParser.Parse(innerHtml, fallbackYear: year);
        if (holidays.Count == 0)
            throw new InvalidOperationException("Parsed 0 holidays from the page HTML.");

        await WriteBlobAsync(year, holidays, ct);
        return holidays;
    }

    private async Task<string?> FetchPageInnerHtmlAsync(CancellationToken ct)
    {
        var credential = new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions
        {
            TenantId = _graph.TenantId,
            ClientId = _graph.ClientId,
            RedirectUri = new Uri("http://localhost")
        });

        var token = await credential.GetTokenAsync(
            new TokenRequestContext(["https://graph.microsoft.com/.default"]), ct);

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        var url = $"https://graph.microsoft.com/v1.0/sites/{_source.SiteId}" +
                  $"/pages/{_source.PageId}/microsoft.graph.sitePage?$expand=canvasLayout";

        var payload = await http.GetStringAsync(url, ct);
        return ExtractInnerHtml(payload);
    }

    private static string? ExtractInnerHtml(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        if (!doc.RootElement.TryGetProperty("canvasLayout", out var canvas)) return null;
        if (!canvas.TryGetProperty("horizontalSections", out var sections)) return null;

        foreach (var section in sections.EnumerateArray())
        {
            if (!section.TryGetProperty("columns", out var columns)) continue;
            foreach (var column in columns.EnumerateArray())
            {
                if (!column.TryGetProperty("webparts", out var webparts)) continue;
                foreach (var wp in webparts.EnumerateArray())
                {
                    var type = wp.TryGetProperty("@odata.type", out var t) ? t.GetString() : null;
                    if (type == "#microsoft.graph.textWebPart" &&
                        wp.TryGetProperty("innerHtml", out var html))
                    {
                        return html.GetString();
                    }
                }
            }
        }
        return null;
    }

    private async Task WriteBlobAsync(int year, IReadOnlyList<LocalDate> holidays, CancellationToken ct)
    {
        var container = new BlobContainerClient(_blob.ConnectionString, _blob.ContainerName);
        await container.CreateIfNotExistsAsync(cancellationToken: ct);

        var dto = new
        {
            year,
            holidays = holidays.Select(d => $"{d.Year:D4}-{d.Month:D2}-{d.Day:D2}").ToArray()
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(dto, Json);

        var blob = container.GetBlobClient($"holidays-{year}.json");
        await blob.UploadAsync(new BinaryData(bytes), overwrite: true, ct);
    }
}

public sealed record SharePointHolidaySource(
    string SiteId,
    string PageId);