namespace Core.Tests;

public class PlanLinksTests
{
    private static AdoConfig Ado() =>
        new("tr-tax", "TaxProf", "TaxProf Team", "pat", new[] { "team-guid" });

    private static PlanEvent Ev(Milestone m, int y, int mo, int d) =>
        new(m, new LocalDate(y, mo, d), false, null);

    private static DeliveryPlan PlanWith(string? id, IReadOnlyList<string>? tags) =>
        new(new Sprint(new LocalDate(2026, 9, 1), "[GFR][2026] - September QED"),
            new[] { Ev(Milestone.StartDev, 2026, 8, 24), Ev(Milestone.QedDeploy, 2026, 9, 14) },
            id, tags);

    [Fact]
    public void AdoPlanLinks_DeliveryPlanUrl_UsesStableRoute()
    {
        var url = AdoPlanLinks.DeliveryPlanUrl(Ado(), "abc-123");
        Assert.Equal("https://dev.azure.com/tr-tax/TaxProf/_deliveryplans/plan/abc-123", url);
    }

    [Fact]
    public void AdoPlanLinks_TicketStatusQueryUrl_EmbedsAllTagsAsOredWiql()
    {
        var url = AdoPlanLinks.TicketStatusQueryUrl(Ado(), new[] { "2026.09_QED", "2026.05" });

        Assert.StartsWith("https://dev.azure.com/tr-tax/TaxProf/_queries/query/?wiql=", url);
        var wiql = Uri.UnescapeDataString(url.Split("wiql=")[1]);
        Assert.Contains("[System.Tags] CONTAINS '2026.09_QED'", wiql);
        Assert.Contains("[System.Tags] CONTAINS '2026.05'", wiql);
        Assert.Contains(" OR ", wiql);
        Assert.Contains("@project", wiql);
    }

    [Fact]
    public void Render_RealPlan_BuildsPerReleaseDeepLinks()
    {
        var plan = PlanWith("plan-999", new[] { "2026.09_QED" });
        var html = new HtmlEmailRenderer(EmailBranding.Default, Ado()).Render(plan, new LocalDate(2026, 9, 1));

        Assert.Contains("_deliveryplans/plan/plan-999", html);
        Assert.Contains("_queries/query/?wiql=", html);
        Assert.DoesNotContain("href=\"#\"", html);
    }

    [Fact]
    public void Render_ConfiguredUrls_OverrideDynamicLinks()
    {
        var branding = EmailBranding.Default with
        {
            DashboardUrl = "https://example.com/dash",
            TicketStatusUrl = "https://example.com/query"
        };
        var plan = PlanWith("plan-999", new[] { "2026.09_QED" });

        var html = new HtmlEmailRenderer(branding, Ado()).Render(plan, new LocalDate(2026, 9, 1));

        Assert.Contains("https://example.com/dash", html);
        Assert.Contains("https://example.com/query", html);
        Assert.DoesNotContain("_deliveryplans/plan/plan-999", html);
    }

    [Fact]
    public void Render_ComputedPlan_NoSourceOrConfig_FallsBackToHash()
    {
        var plan = PlanWith(id: null, tags: null);
        var html = new HtmlEmailRenderer(EmailBranding.Default, Ado()).Render(plan, new LocalDate(2026, 9, 1));

        Assert.Contains("href=\"#\"", html);
        Assert.DoesNotContain("_deliveryplans/plan/", html);
    }

    [Fact]
    public void ParsePlan_CapturesIdAndTagsFromCriteria()
    {
        const string json = """
        {
          "id": "98736a0c-e856-4ea8-8042-0fa62f86524c",
          "name": "[GFR][2026][Delivery Plan] - September QED Release",
          "properties": {
            "markers": [
              {"date":"2026-09-14T00:00:00Z","label":"QED - Deployment","color":"#60AF49"}
            ],
            "criteria": [
              {"fieldName":"System.Tags","operator":"CONTAINS","value":"2026.09_QED"},
              {"fieldName":"System.Tags","operator":"CONTAINS","value":"2026.05","index":1}
            ]
          }
        }
        """;

        var plan = AdoDeliveryPlanReader.ParsePlan(json);

        Assert.Equal("98736a0c-e856-4ea8-8042-0fa62f86524c", plan.PlanId);
        Assert.NotNull(plan.Tags);
        Assert.Equal(new[] { "2026.09_QED", "2026.05" }, plan.Tags);
    }
}
