using Microsoft.Extensions.Configuration;
using System.Reflection;

// Configuration precedence: appsettings.json < user-secrets < environment variables.
// Secrets (the ADO PAT) live in user-secrets or env vars — never in appsettings/source.
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true)
    .AddEnvironmentVariables()
    .Build();

string Setting(string key, string fallback) =>
    configuration[key] is { Length: > 0 } v ? v : fallback;

// 1) Read the Delivery Plan markers straight from Azure DevOps.
//    PAT comes from user-secrets ("Ado:Pat") or the ADO_PAT env var, in that order.
var pat = configuration["Ado:Pat"]
    ?? Environment.GetEnvironmentVariable("ADO_PAT")
    ?? "";

if (string.IsNullOrWhiteSpace(pat))
{
    Console.WriteLine("Missing ADO PAT. Set it with 'dotnet user-secrets set \"Ado:Pat\" <pat>' or the ADO_PAT environment variable.");
    return;
}

var config = new AdoConfig(
    Organization: Setting("Ado:Organization", "tr-tax"),
    Project: Setting("Ado:Project", "TaxProf"),
    Team: Setting("Ado:Team", "TaxProf Team"),
    Pat: pat,
    BaseUrl: Setting("Ado:BaseUrl", "https://dev.azure.com"),
    ApiVersion: Setting("Ado:ApiVersion", "7.1"));

// Configurable owner/text filter — today "mariana", tomorrow any owner/team.
var planFilter = configuration["Ado:PlanFilter"]
    ?? Environment.GetEnvironmentVariable("ADO_PLAN_FILTER")
    ?? "mariana";

using var http = new HttpClient();

// 1a) Find the plans that match the filter (owner or name)
var catalog = new AdoDeliveryPlanCatalog(http, config);
var matches = await catalog.FindPlansAsync(planFilter);

Console.WriteLine($"Plans matching '{planFilter}': {matches.Count}");
foreach (var p in matches.Take(5))
    Console.WriteLine($"  - {p.Name}  ({p.Owner})");
Console.WriteLine(new string('-', 48));

if (matches.Count == 0)
{
    Console.WriteLine("No plans matched the filter. Nothing to render.");
    return;
}

// 1b) Pick the plan to render.
//     Real ADO data: full "Release" plans have all 7 markers; QED-only plans
//     (Jan/Feb/Mar/Sep) end at QED deployment. The renderer supports both, so we
//     just need a plan with at least the QED marker — unless ADO_PLAN_ID forces one.
var planReader = new AdoDeliveryPlanReader(http, config);

var forcedId = configuration["Ado:PlanId"] ?? Environment.GetEnvironmentVariable("ADO_PLAN_ID");
DeliveryPlan? plan = null;

if (!string.IsNullOrWhiteSpace(forcedId))
{
    plan = await planReader.GetPlanAsync(forcedId);
}
else
{
    foreach (var candidate in matches)
    {
        var p = await planReader.GetPlanAsync(candidate.Id);
        if (p.Events.Any(e => e.Label == Milestone.QedDeploy))
        {
            plan = p;
            break;
        }
        Console.WriteLine($"  (skipped incomplete plan: {candidate.Name})");
    }
}

if (plan is null)
{
    Console.WriteLine("No complete plan found to render.");
    return;
}

Console.WriteLine($"Plan: {plan.Sprint.SprintId}");
Console.WriteLine(new string('-', 48));

// 2) show the dates read from ADO
foreach (var e in plan.Events)
{
    Console.WriteLine($"  {e.Label,-18} {e.Date}");
}

// 4) Render the HTML card from the ADO data. Sender identity and links come
//    from configuration (Email:*) so this isn't tied to a single person/team.
var branding = new EmailBranding(
    SenderName: Setting("Email:SenderName", EmailBranding.Default.SenderName),
    SenderTitle: Setting("Email:SenderTitle", EmailBranding.Default.SenderTitle),
    DashboardUrl: Setting("Email:DashboardUrl", EmailBranding.Default.DashboardUrl),
    TicketStatusUrl: Setting("Email:TicketStatusUrl", EmailBranding.Default.TicketStatusUrl));
var renderer = new HtmlEmailRenderer(branding);
var today = LocalDate.FromDateTime(DateTime.Today);
var html = renderer.Render(plan, today);

// Guardar a un archivo para abrirlo
var outputPath = "delivery-plan-email.html";
File.WriteAllText(outputPath, html);

Console.WriteLine();
Console.WriteLine($"Correo generado: {Path.GetFullPath(outputPath)}");

// 5) Create the Outlook draft via Microsoft Graph.
//     Opt-in via config (Graph:CreateDraft) or ADO_CREATE_DRAFT=1; opens the browser for login.
//     Kept behind a flag so a pending Graph consent never breaks the read/render flow.
var createDraft = (configuration["Graph:CreateDraft"]
    ?? Environment.GetEnvironmentVariable("ADO_CREATE_DRAFT")
    ?? "") is "1" or "true" or "True";
if (createDraft)
{
    try
    {
        var graphConfig = new GraphConfig(
            TenantId: Setting("Graph:TenantId", ""),
            ClientId: Setting("Graph:ClientId", ""));

        var draftCreator = new GraphDraftCreator(graphConfig);

        // Subject reflects the plan type: PROD "Release" vs "QED Deployment".
        var hasRelease = plan.Events.Any(e => e.Label == Milestone.Release);
        var goal = plan.Events.First(e => e.Label == (hasRelease ? Milestone.Release : Milestone.QedDeploy));
        var monthName = System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(goal.Date.Month);
        var kind = hasRelease ? "Release" : "QED Deployment";
        var subject = $"[GFR] Delivery Plan — {monthName} {kind}";

        Console.WriteLine();
        Console.WriteLine("Creando borrador... (se abrirá el navegador para login)");

        await draftCreator.CreateDraftAsync(subject: subject, htmlBody: html);

        Console.WriteLine("✅ Borrador creado. Revisa tu carpeta Drafts en Outlook.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"⚠️  Graph aún bloqueado: {ex.Message}");
    }
}
else
{
    Console.WriteLine("(borrador de Outlook omitido; ADO_CREATE_DRAFT=1 para crearlo)");
}

// 6) create a ring image for the next milestone
var ringBytes = RingImageGenerator.CreateRingPng(34);
File.WriteAllBytes("ring-test.png", ringBytes);
Console.WriteLine($"Anillo generado: {Path.GetFullPath("ring-test.png")}");