var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

// --- ADO wiring (registered once, injected into every endpoint) ---

builder.Services.AddHttpClient();

// AdoConfig built once from configuration (Ado__* app settings; PAT from env/Key Vault)
builder.Services.AddSingleton(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    string S(string key, string fallback) =>
        cfg[key] is { Length: > 0 } v ? v : fallback;

    var pat = cfg["Ado:Pat"] ?? Environment.GetEnvironmentVariable("ADO_PAT") ?? "";

    return new AdoConfig(
        Organization: S("Ado:Organization", "tr-tax"),
        Project: S("Ado:Project", "TaxProf"),
        Team: S("Ado:Team", "TaxProf Team"),
        Pat: pat,
        BaseUrl: S("Ado:BaseUrl", "https://dev.azure.com"),
        ApiVersion: S("Ado:ApiVersion", "7.1"),
        TeamIds: cfg.GetSection("Ado:TeamIds").Get<string[]>() ?? []);
});

// Adapters — each gets an HttpClient from the factory + the shared AdoConfig
builder.Services.AddTransient<IDeliveryPlanReader>(sp =>
    new AdoDeliveryPlanReader(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(),
        sp.GetRequiredService<AdoConfig>()));

builder.Services.AddTransient<IDeliveryPlanCatalog>(sp =>
    new AdoDeliveryPlanCatalog(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(),
        sp.GetRequiredService<AdoConfig>()));

builder.Services.AddTransient<IDeliveryPlanWriter>(sp =>
    new AdoDeliveryPlanWriter(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(),
        sp.GetRequiredService<AdoConfig>()));

builder.Build().Run();