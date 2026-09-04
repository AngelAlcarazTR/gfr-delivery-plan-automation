using Adapters;
using Core.Ports;
using static Mcp.Tests.PlanFactory;

namespace Mcp.Tests;

// Exercises the create_plan_draft tool over in-memory fakes: it must read the plan,
// render it, build the right subject (PROD "Release" vs busy-season "QED Deployment")
// and hand subject+html to the draft creator port. No Graph/network involved.
public class CreatePlanDraftTests
{
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

    private sealed class RecordingDraftCreator : IDraftCreator
    {
        public string? Subject { get; private set; }
        public string? Html { get; private set; }
        public int Calls { get; private set; }

        public Task CreateDraftAsync(string subject, string htmlBody, CancellationToken ct = default)
        {
            Subject = subject;
            Html = htmlBody;
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingDraftCreator : IDraftCreator
    {
        public Task CreateDraftAsync(string subject, string htmlBody, CancellationToken ct = default)
            => throw new InvalidOperationException("login was canceled");
    }

    // A configured Graph (non-empty tenant/client) so the fast-fail guard passes.
    private static readonly GraphConfig ConfiguredGraph = new("tenant-id", "client-id");

    // A busy-season, QED-only plan (no Release marker), goal in March.
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
            await PlanTools.CreatePlanDraft(
                new FakeReader(InOrderPlan()), new SentinelRenderer(), new RecordingDraftCreator(),
                ConfiguredGraph, planId: "  "));
    }

    [Fact]
    public async Task CreatesDraft_WithRenderedHtml()
    {
        var draft = new RecordingDraftCreator();

        dynamic result = await PlanTools.CreatePlanDraft(
            new FakeReader(InOrderPlan()), new SentinelRenderer(), draft, ConfiguredGraph, planId: "A");

        Assert.Equal(1, draft.Calls);
        Assert.Equal("<html>body</html>", draft.Html);
        Assert.True((bool)result.created);
        Assert.Equal("<html>body</html>".Length, (int)result.bodyLength);
        Assert.Equal("A", (string)result.planId);
    }

    [Fact]
    public async Task ProdPlan_SubjectUsesRelease()
    {
        var draft = new RecordingDraftCreator();

        dynamic result = await PlanTools.CreatePlanDraft(
            new FakeReader(InOrderPlan()), new SentinelRenderer(), draft, ConfiguredGraph, planId: "A");

        // InOrderPlan releases on 2026-09-30 -> September Release.
        Assert.Equal("[GFR] Delivery Plan \u2014 September Release", (string)result.subject);
        Assert.Equal("[GFR] Delivery Plan \u2014 September Release", draft.Subject);
    }

    [Fact]
    public async Task QedOnlyPlan_SubjectUsesQedDeployment()
    {
        var draft = new RecordingDraftCreator();

        dynamic result = await PlanTools.CreatePlanDraft(
            new FakeReader(QedOnlyPlan()), new SentinelRenderer(), draft, ConfiguredGraph, planId: "A");

        // No Release marker -> the QED month (March) drives the subject.
        Assert.Equal("[GFR] Delivery Plan \u2014 March QED Deployment", (string)result.subject);
    }

    [Fact]
    public async Task SubjectOverride_Wins()
    {
        var draft = new RecordingDraftCreator();

        dynamic result = await PlanTools.CreatePlanDraft(
            new FakeReader(InOrderPlan()), new SentinelRenderer(), draft,
            ConfiguredGraph, planId: "A", subject: "Custom subject");

        Assert.Equal("Custom subject", (string)result.subject);
        Assert.Equal("Custom subject", draft.Subject);
    }

    [Fact]
    public async Task PassesTodayToRenderer()
    {
        var renderer = new SentinelRenderer();

        await PlanTools.CreatePlanDraft(
            new FakeReader(InOrderPlan()), renderer, new RecordingDraftCreator(),
            ConfiguredGraph, planId: "A", today: "2026-09-01");

        Assert.Equal(new LocalDate(2026, 9, 1), renderer.Today);
    }

    [Fact]
    public async Task MissingGraphConfig_ReturnsClearError_WithoutCallingGraph()
    {
        var draft = new RecordingDraftCreator();

        dynamic result = await PlanTools.CreatePlanDraft(
            new FakeReader(InOrderPlan()), new SentinelRenderer(), draft,
            new GraphConfig("", ""), planId: "A");

        Assert.False((bool)result.created);
        Assert.Contains("Graph", (string)result.error);
        Assert.Equal(0, draft.Calls);           // never reached the Graph call
    }

    [Fact]
    public async Task GraphThrows_ReturnsRealErrorMessage_NotOpaque()
    {
        dynamic result = await PlanTools.CreatePlanDraft(
            new FakeReader(InOrderPlan()), new SentinelRenderer(), new ThrowingDraftCreator(),
            ConfiguredGraph, planId: "A");

        Assert.False((bool)result.created);
        Assert.Contains("login was canceled", (string)result.error);
    }
}
