using System.Text.Json;

namespace Core.Tests;

// Unit tests for the pure, network-free JSON mutation used by
// AdoDeliveryPlanWriter.UpdateMarkerDateAsync. It must change ONLY the target
// marker's date and preserve everything else in the raw ADO document — including
// markers the domain reader does not track (e.g. "Communicate Release Plan").
public class AdoDeliveryPlanWriterTests
{
    private const string PlanJson = """
    {
      "id": "0e303abe-48c7-49ce-b25c-a97d2491f0c7",
      "name": "[GFR][2026][Delivery Plan] - September 14th QED Release",
      "revision": 7,
      "properties": {
        "markers": [
          {"date":"2026-08-24T00:00:00Z","label":"Start- Development","color":"#71338D"},
          {"date":"2026-09-14T00:00:00Z","label":"QED - Deployment","color":"#60AF49"},
          {"date":"2026-09-20T00:00:00Z","label":"Communicate Release Plan","color":"#000000"}
        ],
        "cardSettings": { "fields": { "showId": false } }
      }
    }
    """;

    [Fact]
    public void ApplyMarkerDate_MovesTargetMarker_ReportsPreviousDate()
    {
        var (json, found, previous, count) = AdoDeliveryPlanWriter.ApplyMarkerDate(
            PlanJson, Milestone.QedDeploy, new LocalDate(2026, 9, 15));

        Assert.True(found);
        Assert.Equal(1, count);
        Assert.Equal(new LocalDate(2026, 9, 14), previous);

        var plan = AdoDeliveryPlanReader.ParsePlan(json);
        Assert.Equal(new LocalDate(2026, 9, 15),
            plan.Events.Single(e => e.Label == Milestone.QedDeploy).Date);
    }

    [Fact]
    public void ApplyMarkerDate_WritesUtcMidnight()
    {
        var (json, _, _, _) = AdoDeliveryPlanWriter.ApplyMarkerDate(
            PlanJson, Milestone.QedDeploy, new LocalDate(2026, 9, 15));

        using var doc = JsonDocument.Parse(json);
        var qed = doc.RootElement.GetProperty("properties").GetProperty("markers")
            .EnumerateArray().Single(m => m.GetProperty("label").GetString() == "QED - Deployment");
        Assert.Equal("2026-09-15T00:00:00Z", qed.GetProperty("date").GetString());
    }

    [Fact]
    public void ApplyMarkerDate_PreservesOtherFieldsAndUntrackedMarkers()
    {
        var (json, _, _, _) = AdoDeliveryPlanWriter.ApplyMarkerDate(
            PlanJson, Milestone.QedDeploy, new LocalDate(2026, 9, 15));

        // Sibling milestone untouched.
        var plan = AdoDeliveryPlanReader.ParsePlan(json);
        Assert.Equal(new LocalDate(2026, 8, 24),
            plan.Events.Single(e => e.Label == Milestone.StartDev).Date);

        // Revision, cardSettings and the untracked marker all survive the rewrite.
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(7, root.GetProperty("revision").GetInt32());

        var props = root.GetProperty("properties");
        Assert.True(props.TryGetProperty("cardSettings", out _));

        var labels = props.GetProperty("markers").EnumerateArray()
            .Select(m => m.GetProperty("label").GetString()).ToList();
        Assert.Equal(3, labels.Count);
        Assert.Contains("Communicate Release Plan", labels);
    }

    [Fact]
    public void ApplyMarkerDate_MarkerAbsent_ReturnsNotFound_AndOriginalJson()
    {
        // The sample plan has no Release marker.
        var (json, found, previous, count) = AdoDeliveryPlanWriter.ApplyMarkerDate(
            PlanJson, Milestone.Release, new LocalDate(2026, 9, 30));

        Assert.False(found);
        Assert.Equal(0, count);
        Assert.Null(previous);
        Assert.Same(PlanJson, json);
    }

    [Fact]
    public void ApplyMarkerDate_NoPropertiesOrMarkers_ReturnsNotFound()
    {
        var (_, found, _, count) = AdoDeliveryPlanWriter.ApplyMarkerDate(
            """{"id":"x","name":"n"}""", Milestone.QedDeploy, new LocalDate(2026, 9, 15));

        Assert.False(found);
        Assert.Equal(0, count);
    }

    [Fact]
    public void ApplyMarkerDate_ToleratesLabelSpacingVariants()
    {
        // "Start- Development" (odd spacing, as ADO stores it) must still match StartDev.
        var (json, found, _, _) = AdoDeliveryPlanWriter.ApplyMarkerDate(
            PlanJson, Milestone.StartDev, new LocalDate(2026, 8, 25));

        Assert.True(found);
        var plan = AdoDeliveryPlanReader.ParsePlan(json);
        Assert.Equal(new LocalDate(2026, 8, 25),
            plan.Events.Single(e => e.Label == Milestone.StartDev).Date);
    }
}
