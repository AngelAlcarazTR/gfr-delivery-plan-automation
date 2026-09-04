using Adapters;
using Core.Ports;
using static Mcp.Tests.PlanFactory;

namespace Mcp.Tests;

// Exercises the export_plan_email tool: it reads the plan, renders it and writes a
// self-contained .eml (X-Unsent compose mode) to disk. No Graph, no network. Files
// are written into a throwaway temp directory that is cleaned up per test.
public class ExportPlanEmailTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "eml-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private string Out(string name) => Path.Combine(_dir, name);

    // --- fakes -------------------------------------------------------------

    private sealed class FakeReader(DeliveryPlan plan) : IDeliveryPlanReader
    {
        public Task<DeliveryPlan> GetPlanAsync(string planId, CancellationToken ct = default)
            => Task.FromResult(plan);
    }

    private sealed class SentinelRenderer : IDeliveryPlanRenderer
    {
        public LocalDate Today { get; private set; }
        public string Render(DeliveryPlan plan, LocalDate today)
        {
            Today = today;
            return "<html>body</html>";
        }
    }

    private static DeliveryPlan QedOnlyPlan() => Plan(
        (Milestone.StartDev, D(2026, 2, 2)),
        (Milestone.EndDev, D(2026, 2, 20)),
        (Milestone.QaCutoff, D(2026, 2, 27)),
        (Milestone.QedDeploy, D(2026, 3, 5)));

    // --- tests -------------------------------------------------------------

    [Fact]
    public async Task EmptyPlanId_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await PlanTools.ExportPlanEmail(
                new FakeReader(InOrderPlan()), new SentinelRenderer(), planId: "  "));
    }

    [Fact]
    public async Task WritesEmlFile_WithRenderedBody()
    {
        var path = Out("plan.eml");

        dynamic result = await PlanTools.ExportPlanEmail(
            new FakeReader(InOrderPlan()), new SentinelRenderer(),
            planId: "A", outputPath: path);

        Assert.True(File.Exists(path));
        Assert.Equal(Path.GetFullPath(path), (string)result.path);
        Assert.True((long)result.bytesWritten > 0);

        // The clickable link must be a file:// URI pointing at the same file.
        Assert.StartsWith("file:///", (string)result.fileUrl);
        Assert.Equal(new Uri(Path.GetFullPath(path)).AbsoluteUri, (string)result.fileUrl);

        var eml = await File.ReadAllTextAsync(path);
        Assert.Contains("X-Unsent: 1\r\n", eml);

        // The base64 body must decode to the rendered HTML.
        var idx = eml.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var b64 = eml[(idx + 4)..].Replace("\r\n", "");
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
        Assert.Equal("<html>body</html>", decoded);
    }

    [Fact]
    public async Task IncludesRecipient_WhenProvided()
    {
        var path = Out("plan.eml");

        dynamic result = await PlanTools.ExportPlanEmail(
            new FakeReader(InOrderPlan()), new SentinelRenderer(),
            planId: "A", to: "you@thomsonreuters.com", outputPath: path);

        Assert.Equal("you@thomsonreuters.com", (string)result.to);
        var eml = await File.ReadAllTextAsync(path);
        Assert.Contains("To: you@thomsonreuters.com\r\n", eml);
    }

    [Fact]
    public async Task ProdPlan_SubjectUsesRelease()
    {
        dynamic result = await PlanTools.ExportPlanEmail(
            new FakeReader(InOrderPlan()), new SentinelRenderer(),
            planId: "A", outputPath: Out("plan.eml"));

        Assert.Equal("[GFR] Delivery Plan \u2014 September Release", (string)result.subject);
    }

    [Fact]
    public async Task QedOnlyPlan_SubjectUsesQedDeployment()
    {
        dynamic result = await PlanTools.ExportPlanEmail(
            new FakeReader(QedOnlyPlan()), new SentinelRenderer(),
            planId: "A", outputPath: Out("plan.eml"));

        Assert.Equal("[GFR] Delivery Plan \u2014 March QED Deployment", (string)result.subject);
    }

    [Fact]
    public async Task SubjectOverride_Wins()
    {
        dynamic result = await PlanTools.ExportPlanEmail(
            new FakeReader(InOrderPlan()), new SentinelRenderer(),
            planId: "A", subject: "Custom subject", outputPath: Out("plan.eml"));

        Assert.Equal("Custom subject", (string)result.subject);
    }

    [Fact]
    public async Task PassesTodayToRenderer()
    {
        var renderer = new SentinelRenderer();

        await PlanTools.ExportPlanEmail(
            new FakeReader(InOrderPlan()), renderer,
            planId: "A", today: "2026-09-01", outputPath: Out("plan.eml"));

        Assert.Equal(new LocalDate(2026, 9, 1), renderer.Today);
    }

    [Fact]
    public async Task CreatesOutputDirectory_WhenMissing()
    {
        // A nested path whose directory does not exist yet.
        var path = Out(Path.Combine("nested", "sub", "plan.eml"));

        await PlanTools.ExportPlanEmail(
            new FakeReader(InOrderPlan()), new SentinelRenderer(),
            planId: "A", outputPath: path);

        Assert.True(File.Exists(path));
    }
}
