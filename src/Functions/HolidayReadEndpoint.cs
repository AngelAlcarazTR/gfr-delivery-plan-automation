namespace Functions;

// GET /api/holidays/{year}  — TEST endpoint: reads holidays from the Blob and
// returns them. Confirms BlobHolidayReader works before wiring it to the engine.
public class HolidayReadEndpoint(ILogger<HolidayReadEndpoint> logger, IHolidayReader reader)
{
    private readonly ILogger<HolidayReadEndpoint> _logger = logger;
    private readonly IHolidayReader _reader = reader;

    [Function("HolidayRead")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "holidays/{year:int}")] HttpRequest req,
        int year,
        CancellationToken ct)
    {
        _logger.LogInformation("Reading holidays for {Year} from Blob...", year);

        var holidays = await _reader.GetHolidaysAsync(year, ct);

        if (holidays is null)
            return new NotFoundObjectResult($"No holidays blob found for {year}.");

        return new OkObjectResult(new
        {
            year,
            count = holidays.Count,
            holidays = holidays.Select(d => $"{d.Year:D4}-{d.Month:D2}-{d.Day:D2}")
        });
    }
}