var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

builder.Services.AddHttpClient();

builder.Services.AddSingleton(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    string S(string key, string fallback) => cfg[key] is { Length: > 0 } v ? v : fallback;
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

// Holidays: options + reader (Blob-first; the tool falls back to CompanyHolidays)
builder.Services.AddSingleton(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    return new BlobHolidayReaderOptions(ConnectionString: cfg["HolidaysStorage"] ?? "");
});
builder.Services.AddTransient<IHolidayReader, BlobHolidayReader>();
builder.Services.AddTransient<IHolidayWriter, BlobHolidayWriter>();
builder.Services.AddTransient<IHolidayCalendarSource, BlobHolidayCalendarSource>();

builder.Services.AddTransient<IDeliveryPlanCatalog>(sp =>
    new AdoDeliveryPlanCatalog(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(),
        sp.GetRequiredService<AdoConfig>()));

builder.Services.AddTransient<IDeliveryPlanReader>(sp =>
    new AdoDeliveryPlanReader(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(),
        sp.GetRequiredService<AdoConfig>()));

builder.Services.AddTransient<IDeliveryPlanWriter>(sp =>
    new AdoDeliveryPlanWriter(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient(),
        sp.GetRequiredService<AdoConfig>()));

// Branding + footer links are configuration (same keys as the Functions host).
// Leave Email__DashboardUrl / Email__TicketStatusUrl unset to get per-release
// deep-links built from the plan's own id/tags.
builder.Services.AddSingleton(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    return new EmailBranding(
        SenderName: cfg["Email:SenderName"] ?? EmailBranding.Default.SenderName,
        SenderTitle: cfg["Email:SenderTitle"] ?? EmailBranding.Default.SenderTitle,
        DashboardUrl: cfg["Email:DashboardUrl"] ?? "",
        TicketStatusUrl: cfg["Email:TicketStatusUrl"] ?? "");
});

// Same renderer the Azure Function uses, so get_plan_render returns identical HTML
// in-process (no HTTP hop, no dependency on the Function being deployed/reachable).
builder.Services.AddTransient<IDeliveryPlanRenderer>(sp =>
    new HtmlEmailRenderer(
        sp.GetRequiredService<EmailBranding>(),
        sp.GetRequiredService<AdoConfig>()));

var app = builder.Build();

app.MapMcp();

app.Run();