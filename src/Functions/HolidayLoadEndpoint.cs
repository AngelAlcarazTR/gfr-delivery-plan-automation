namespace Functions;

public class HolidayLoadEndpoint(ILogger<HolidayLoadEndpoint> logger, SharePointHolidayLoader loader)
{
    private readonly ILogger<HolidayLoadEndpoint> _logger = logger;
    private readonly SharePointHolidayLoader _loader = loader;

    [Function("HolidayLoad")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "holidays/load")] HttpRequest req,
        CancellationToken ct)
    {
        var year = int.TryParse(req.Query["year"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y)
            ? y
            : DateTime.UtcNow.Year;

        _logger.LogInformation("Loading holidays for {Year} from SharePoint...", year);

        try
        {
            var holidays = await _loader.LoadAsync(year, ct);

            return new OkObjectResult(new
            {
                year,
                count = holidays.Count,
                holidays = holidays.Select(d => $"{d.Year:D4}-{d.Month:D2}-{d.Day:D2}"),
                message = $"Wrote holidays-{year}.json to Blob."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Holiday load failed for {Year}", year);
            return new ObjectResult($"Holiday load failed: {ex.Message}") { StatusCode = 502 };
        }
    }
}