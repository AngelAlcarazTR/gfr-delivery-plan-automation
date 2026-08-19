namespace Functions;  

// GET /api/plan-ado  — Azure shell that reads the REAL plan from ADO.
// Config comes from app settings (Ado__*), PAT included. Optional ?filter= override.
public class PlanAdoEndpoint(ILogger<PlanAdoEndpoint> logger, IConfiguration configuration)
{
    private readonly ILogger<PlanAdoEndpoint> _logger = logger;
    private readonly IConfiguration _config = configuration;

    [Function("PlanAdo")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "plan-ado")] HttpRequest req,
        CancellationToken ct)
    {
        string Setting(string key, string fallback) =>
            _config[key] is { Length: > 0 } v ? v : fallback;

        var pat = _config["Ado:Pat"] ?? "";
        if (string.IsNullOrWhiteSpace(pat))
            return new ObjectResult("Missing 'Ado__Pat' app setting.") { StatusCode = 400 };

        // Filter: ?filter= wins, else app setting, else "mariana".
        var planFilter = req.Query["filter"].ToString() is { Length: > 0 } f
            ? f
            : Setting("Ado:PlanFilter", "mariana");

        var adoConfig = new AdoConfig(
            Organization: Setting("Ado:Organization", "tr-tax"),
            Project: Setting("Ado:Project", "TaxProf"),
            Team: Setting("Ado:Team", "TaxProf Team"),
            Pat: pat,
            BaseUrl: Setting("Ado:BaseUrl", "https://dev.azure.com"),
            ApiVersion: Setting("Ado:ApiVersion", "7.1"));

        _logger.LogInformation("Reading plan from ADO with filter '{Filter}'", planFilter);

        try
        {
            using var http = new HttpClient();
            var catalog = new AdoDeliveryPlanCatalog(http, adoConfig);
            var reader = new AdoDeliveryPlanReader(http, adoConfig);

            var job = new DeliveryPlanJob(new HtmlEmailRenderer());
            var html = await job.GenerateFromSourceAsync(
                catalog, reader, planFilter, LocalDate.FromDateTime(DateTime.Today), ct);

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