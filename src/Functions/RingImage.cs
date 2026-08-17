namespace Functions;

public class RingImage(ILogger<RingImage> logger)
{
    private readonly ILogger<RingImage> _logger = logger;

    [Function("RingImage")]
    public IActionResult Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ring")] HttpRequest req)
    {
        _logger.LogInformation("Generando anillo PNG");

        var percent = 34;
        if (int.TryParse(req.Query["percent"], out var p))
            percent = Math.Clamp(p, 0, 100);

        var png = RingImageGenerator.CreateRingPng(percent);
        return new FileContentResult(png, "image/png");
    }
}