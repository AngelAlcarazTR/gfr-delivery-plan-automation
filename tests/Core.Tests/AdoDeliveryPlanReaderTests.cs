namespace Core.Tests;

public class AdoDeliveryPlanReaderTests
{
    // Real JSON returned by ADO for plan 98736a0c... ([GFR][2026] - August 24th Release)
    private const string RealPlanJson = """
    {
      "id": "98736a0c-e856-4ea8-8042-0fa62f86524c",
      "name": "[GFR][2026][Delivery Plan] - August 24th Release",
      "properties": {
        "markers": [
          {"date":"2026-07-20T00:00:00Z","label":"Start - Development","color":"#EF33A3"},
          {"date":"2026-08-04T00:00:00Z","label":"End - Development","color":"#71338D"},
          {"date":"2026-08-07T00:00:00Z","label":"QA - Cut-off","color":"#E87025"},
          {"date":"2026-08-10T00:00:00Z","label":"QED - Deployment","color":"#60AF49"},
          {"date":"2026-08-10T00:00:00Z","label":"Start - Regression testing","color":"#FBD144"},
          {"date":"2026-08-18T00:00:00Z","label":"End - Regression testing","color":"#43B4D5"},
          {"date":"2026-08-24T00:00:00Z","label":"AMER/UK - Release date","color":"#1B478B"}
        ]
      }
    }
    """;

    [Fact]
    public void ParsePlan_RealAdoResponse_MapsSevenMarkersToMilestones()
    {
        var plan = AdoDeliveryPlanReader.ParsePlan(RealPlanJson);
        var d = plan.Events.ToDictionary(e => e.Label, e => e.Date);

        Assert.Equal("[GFR][2026][Delivery Plan] - August 24th Release", plan.Sprint.SprintId);
        Assert.Equal(7, plan.Events.Count);

        Assert.Equal(new LocalDate(2026, 7, 20), d[Milestone.StartDev]);
        Assert.Equal(new LocalDate(2026, 8, 4), d[Milestone.EndDev]);
        Assert.Equal(new LocalDate(2026, 8, 7), d[Milestone.QaCutoff]);
        Assert.Equal(new LocalDate(2026, 8, 10), d[Milestone.QedDeploy]);
        Assert.Equal(new LocalDate(2026, 8, 10), d[Milestone.StartReg]);
        Assert.Equal(new LocalDate(2026, 8, 18), d[Milestone.EndReg]);
        Assert.Equal(new LocalDate(2026, 8, 24), d[Milestone.Release]);
    }

    [Fact]
    public void ParsePlan_KeepsMidnightUtcDate_WithoutTimezoneShift()
    {
        var plan = AdoDeliveryPlanReader.ParsePlan(RealPlanJson);
        var start = plan.Events.First(e => e.Label == Milestone.StartDev).Date;

        // 2026-07-20T00:00:00Z must stay July 20 (a local-time cast would roll it to July 19).
        Assert.Equal(new LocalDate(2026, 7, 20), start);
    }

    [Fact]
    public void ParsePlan_OrdersEventsChronologically()
    {
        var plan = AdoDeliveryPlanReader.ParsePlan(RealPlanJson);

        for (var i = 1; i < plan.Events.Count; i++)
            Assert.True(plan.Events[i].Date >= plan.Events[i - 1].Date);
    }

    [Theory]
    [InlineData("Start- Development", Milestone.StartDev)]
    [InlineData("Start - Development", Milestone.StartDev)]
    [InlineData("End - Development / Dev CutOff", Milestone.EndDev)]
    [InlineData("QA - Cut-off", Milestone.QaCutoff)]
    [InlineData("QA - CutOff", Milestone.QaCutoff)]
    [InlineData("QED - Deployment", Milestone.QedDeploy)]
    [InlineData("QED - Second Deployment", Milestone.QedDeploy)]
    [InlineData("Start - Regression testing", Milestone.StartReg)]
    [InlineData("Start QED Regression Testing", Milestone.StartReg)]
    [InlineData("End - QED Regression Testing", Milestone.EndReg)]
    [InlineData("AMER/UK - Release date", Milestone.Release)]
    [InlineData("UK - Release Date", Milestone.Release)]
    public void MapLabel_KnownVariants_MapToExpectedMilestone(string label, Milestone expected)
    {
        Assert.Equal(expected, AdoDeliveryPlanReader.MapLabel(label));
    }

    [Theory]
    [InlineData("Communicate Release Plan")]
    [InlineData("QA - Approval")]
    [InlineData("QA - SignOff")]
    [InlineData("QA Buffer Day")]
    public void MapLabel_UntrackedLabels_ReturnNull(string label)
    {
        Assert.Null(AdoDeliveryPlanReader.MapLabel(label));
    }
}
