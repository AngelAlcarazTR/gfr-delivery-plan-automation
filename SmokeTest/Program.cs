using Core.Application;
using Core.Domain;

// 1) Read the current sprint from ADO
var config = new AdoConfig(
    Organization: "tr-tax",
    Project: "TaxProf",
    Team: "TaxProf Team",
    Pat: Environment.GetEnvironmentVariable("ADO_PAT") ?? "");

using var http = new HttpClient();
var reader = new AdoSprintReader(http, config);
var sprint = await reader.GetCurrentSprintAsync();

Console.WriteLine($"Sprint: {sprint.SprintId} (inicia {sprint.StartDate})");
Console.WriteLine(new string('-', 48));

// 2) calculate the delivery plan for the current sprint
var engineConfig = new EngineConfig(
    DevelopmentDays: 12,
    QaCutoffGapDays: 3,
    QedGapDays: 1,
    RegressionGapDays: 0,
    RegressionDays: 7);

var plan = DeliveryPlanCalculator.Compute(sprint, engineConfig);

// 3) show the delivery plan in the console
foreach (var e in plan.Events)
{
    Console.WriteLine($"  {e.Label,-18} {e.Date}");
}

// 4) Renderizar el correo HTML
var renderer = new HtmlEmailRenderer();
var html = renderer.Render(plan);

// Guardar a un archivo para abrirlo
var outputPath = "delivery-plan-email.html";
File.WriteAllText(outputPath, html);

Console.WriteLine();
Console.WriteLine($"Correo generado: {Path.GetFullPath(outputPath)}");

// 5) Crear el borrador en Outlook vía Graph
var graphConfig = new GraphConfig(
    TenantId: "62ccb864-6a1a-4b5d-8e1c-397dec1a8258",
    ClientId: "ad58cf49-76b5-4051-b5d3-3ba6a7462bc0");

var draftCreator = new GraphDraftCreator(graphConfig);

Console.WriteLine();
Console.WriteLine("Creando borrador... (se abrirá el navegador para login)");

await draftCreator.CreateDraftAsync(
    subject: "[TEST Graph] Delivery Plan - August release",
    htmlBody: html);

Console.WriteLine("✅ Borrador creado. Revisa tu carpeta Drafts en Outlook.");