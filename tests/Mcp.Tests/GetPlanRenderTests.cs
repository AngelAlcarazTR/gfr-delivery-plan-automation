using Adapters;
using Core.Ports;
using static Mcp.Tests.PlanFactory;

namespace Mcp.Tests;

// Exercises the get_plan_render tool over in-memory fakes: it must read the plan
// by id and hand it to the renderer port, echoing back the HTML artifact. The
// real HtmlEmailRenderer is used in one test to prove a complete, self-contained
// document comes out (no network, no deployed Function involved).
public class GetPlanRenderTests
{
    // --- fakes -------------------------------------------------------------

    private sealed class FakeReader(DeliveryPlan plan) : IDeliveryPlanReader
    {
        public string? RequestedId { get; private set; }

        public Task<DeliveryPlan> GetPlanAsync(string planId, CancellationToken ct = default)
        {
            RequestedId = planId;
            return Task.FromResult(plan);
        }
    }

    // Records what it was asked to render and returns a sentinel string.
    private sealed class RecordingRenderer : IDeliveryPlanRenderer
    {
        public DeliveryPlan? Plan { get; private set; }
        public LocalDate Today { get; private set; }

        public string Render(DeliveryPlan plan, LocalDate today)
        {
            Plan = plan;
            Today = today;
            return "<html>sentinel</html>";
        }
    }

    // --- tests -------------------------------------------------------------

    [Fact]
    public async Task EmptyPlanId_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await PlanTools.GetPlanRender(
                new FakeReader(InOrderPlan()), new RecordingRenderer(), planId: "  "));
    }

    [Fact]
    public async Task ReadsRequestedPlanId_AndRendersThatPlan()
    {
        var plan = InOrderPlan();
        var reader = new FakeReader(plan);
        var renderer = new RecordingRenderer();

        await PlanTools.GetPlanRender(reader, renderer, planId: "PLAN-42");

        Assert.Equal("PLAN-42", reader.RequestedId);
        Assert.Same(plan, renderer.Plan);
    }

    [Fact]
    public async Task DefaultsTodayToToday()
    {
        var renderer = new RecordingRenderer();

        dynamic result = await PlanTools.GetPlanRender(
            new FakeReader(InOrderPlan()), renderer, planId: "A");

        var expected = LocalDate.FromDateTime(DateTime.Today);
        Assert.Equal(expected, renderer.Today);
        Assert.Equal($"{expected.Year:D4}-{expected.Month:D2}-{expected.Day:D2}", (string)result.today);
    }

    [Fact]
    public async Task UsesProvidedToday()
    {
        var renderer = new RecordingRenderer();

        dynamic result = await PlanTools.GetPlanRender(
            new FakeReader(InOrderPlan()), renderer, planId: "A", today: "2026-09-01");

        Assert.Equal(new LocalDate(2026, 9, 1), renderer.Today);
        Assert.Equal("2026-09-01", (string)result.today);
    }

    [Fact]
    public async Task EchoesRenderedHtml_AndMetadata()
    {
        dynamic result = await PlanTools.GetPlanRender(
            new FakeReader(InOrderPlan()), new RecordingRenderer(), planId: "A");

        Assert.Equal("A", (string)result.planId);
        Assert.Equal("text/html", (string)result.contentType);
        Assert.Equal("<html>sentinel</html>", (string)result.html);
        Assert.Equal("<html>sentinel</html>".Length, (int)result.length);
    }

    [Fact]
    public async Task RealRenderer_ProducesSelfContainedHtmlDocument()
    {
        // No branding/ado -> footer links fall back, but the document must still be
        // a complete HTML page with the base64 progress ring embedded.
        dynamic result = await PlanTools.GetPlanRender(
            new FakeReader(InOrderPlan()), new HtmlEmailRenderer(), planId: "A", today: "2026-08-15");

        string html = (string)result.html;
        Assert.Contains("<!DOCTYPE html", html);
        Assert.Contains("</html>", html);
        Assert.Contains("data:image/png;base64,", html);   // embedded ring, no external asset
        Assert.True((int)result.length > 500);
    }
}
