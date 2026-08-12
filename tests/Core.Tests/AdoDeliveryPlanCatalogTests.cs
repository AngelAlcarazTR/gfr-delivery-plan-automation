namespace Core.Tests;

public class AdoDeliveryPlanCatalogTests
{
    // Real-shaped JSON from GET /_apis/work/plans (3 representative entries).
    private const string PlanListJson = """
    {
      "count": 3,
      "value": [
        {
          "id": "98736a0c-e856-4ea8-8042-0fa62f86524c",
          "name": "[GFR][2026][Delivery Plan] - August 24th Release",
          "type": "deliveryTimelineView",
          "createdDate": "2026-06-01T10:00:00Z",
          "createdByIdentity": { "displayName": "Moser, Mariana (TR Technology)" },
          "modifiedDate": "2026-07-15T12:30:00Z",
          "modifiedByIdentity": { "displayName": "Moser, Mariana (TR Technology)" }
        },
        {
          "id": "c1575498-18d8-4ae3-b96d-004fe4a525fa",
          "name": "GFR September 29th 2023 Delivery Plan - QED Release",
          "type": "deliveryTimelineView",
          "createdDate": "2023-08-14T18:41:18Z",
          "createdByIdentity": { "displayName": "Pulido, Yemmill (TR Technology)" },
          "modifiedDate": "2023-09-14T14:40:03Z",
          "modifiedByIdentity": { "displayName": "Pulido, Yemmill (TR Technology)" }
        },
        {
          "id": "dd13302a-aa2d-44a0-b772-10415612c52b",
          "name": "[GFR][2025][Delivery Plan] - August 25th Release",
          "type": "deliveryTimelineView",
          "createdDate": "2025-06-02T09:00:00Z",
          "createdByIdentity": { "displayName": "Moser, Mariana (TR Technology)" },
          "modifiedDate": "2025-07-20T08:00:00Z",
          "modifiedByIdentity": { "displayName": "Moser, Mariana (TR Technology)" }
        }
      ]
    }
    """;

    [Fact]
    public void ParsePlanList_FilterByMariana_ReturnsOnlyHerPlans()
    {
        var plans = AdoDeliveryPlanCatalog.ParsePlanList(PlanListJson, "mariana");

        Assert.Equal(2, plans.Count);
        Assert.All(plans, p => Assert.Contains("Mariana", p.Owner));
    }

    [Fact]
    public void ParsePlanList_SortsMostRecentlyModifiedFirst()
    {
        var plans = AdoDeliveryPlanCatalog.ParsePlanList(PlanListJson, "mariana");

        // 2026 plan modified after the 2025 one.
        Assert.Equal("[GFR][2026][Delivery Plan] - August 24th Release", plans[0].Name);
        Assert.True(plans[0].ModifiedAt >= plans[1].ModifiedAt);
    }

    [Fact]
    public void ParsePlanList_EmptyFilter_ReturnsAll()
    {
        var plans = AdoDeliveryPlanCatalog.ParsePlanList(PlanListJson, "");
        Assert.Equal(3, plans.Count);
    }

    [Fact]
    public void ParsePlanList_FilterByOtherOwner_ReturnsTheirPlans()
    {
        var plans = AdoDeliveryPlanCatalog.ParsePlanList(PlanListJson, "Pulido");
        Assert.Single(plans);
        Assert.Equal("GFR September 29th 2023 Delivery Plan - QED Release", plans[0].Name);
    }

    [Theory]
    [InlineData("mariana", "x", "Moser, Mariana (TR Technology)", "", true)]  // owner match
    [InlineData("August", "August 24th Release", "Someone", "", true)]         // name match
    [InlineData("mariana", "August Release", "Pulido, Yemmill", "Moser, Mariana", true)] // modifiedBy match
    [InlineData("mariana", "August Release", "Pulido, Yemmill", "Pulido, Yemmill", false)] // no match
    public void Matches_TextAgainstNameOrOwner(
        string filter, string name, string createdBy, string modifiedBy, bool expected)
    {
        Assert.Equal(expected, AdoDeliveryPlanCatalog.Matches(filter, name, createdBy, modifiedBy));
    }
}
