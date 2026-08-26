namespace Functions;

// GET /api/plan-ado — reads the REAL plan from ADO.
// AdoConfig + adapters come from DI (registered in Program.cs).
// Filter precedence: ?filter= query  >  Ado:PlanFilter setting  (no person default).
// TODO US-2: replace this owner/text filter with name/date selection (nearest goal >= today).
public class PlanAdoEndpoint(
    ILogger<PlanAdoEndpoint> logger,
    IConfiguration configuration,
    IDeliveryPlanReader reader,
    IDeliveryPlanCatalog catalog,
    IDeliveryPlanRenderer renderer)
{
    private readonly ILogger<PlanAdoEndpoint> _logger = logger;
    private readonly IConfiguration _config = configuration;
    private readonly IDeliveryPlanReader _reader = reader;
    private readonly IDeliveryPlanCatalog _catalog = catalog;
    private readonly IDeliveryPlanRenderer _renderer = renderer;

    [Function("PlanAdo")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "plan-ado")] HttpRequest req,
        CancellationToken ct)
    {
        // Friendly early check (the injected AdoConfig would otherwise fail with 502).
        var pat = _config["Ado:Pat"] ?? Environment.GetEnvironmentVariable("ADO_PAT") ?? "";
        if (string.IsNullOrWhiteSpace(pat))
            return new ObjectResult("Missing 'Ado__Pat' app setting.") { StatusCode = 400 };

        // Filter: ?filter= wins, else Ado:PlanFilter. No hidden person default.
        var planFilter = req.Query["filter"].ToString() is { Length: > 0 } f
            ? f
            : _config["Ado:PlanFilter"];

        if (string.IsNullOrWhiteSpace(planFilter))
            return new ObjectResult("Missing plan filter. Pass ?filter= or set 'Ado__PlanFilter'.")
            { StatusCode = 400 };

        _logger.LogInformation("Reading plan from ADO with filter '{Filter}'", planFilter);

        try
        {
            var job = new DeliveryPlanJob(_renderer);
            var html = await job.GenerateFromSourceAsync(
                _catalog, _reader, planFilter, LocalDate.FromDateTime(DateTime.Today), ct);

            return new ContentResult
            {
                Content = html,
                ContentType = "text/html; charset=utf-8",
                StatusCode = 200
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ADO read failed");
            return new ObjectResult($"ADO read failed: {ex.Message}") { StatusCode = 502 };
        }
    }
}