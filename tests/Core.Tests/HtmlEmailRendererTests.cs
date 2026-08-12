namespace Core.Tests;

public class HtmlEmailRendererTests
{
    private static PlanEvent Ev(Milestone m, int y, int mo, int d) =>
        new(m, new LocalDate(y, mo, d), false, null);

    // A full PROD release: all 7 markers.
    private static DeliveryPlan FullPlan() => new(
        new Sprint(new LocalDate(2026, 7, 20), "[GFR][2026][Delivery Plan] - August 24th Release"),
        new[]
        {
            Ev(Milestone.StartDev, 2026, 7, 20),
            Ev(Milestone.EndDev, 2026, 8, 4),
            Ev(Milestone.QaCutoff, 2026, 8, 7),
            Ev(Milestone.QedDeploy, 2026, 8, 10),
            Ev(Milestone.StartReg, 2026, 8, 10),
            Ev(Milestone.EndReg, 2026, 8, 18),
            Ev(Milestone.Release, 2026, 8, 24),
        });

    // A QED-only plan (Jan/Feb/Mar/Sep): no Regression, no PROD Release.
    private static DeliveryPlan QedOnlyPlan() => new(
        new Sprint(new LocalDate(2026, 1, 5), "[GFR][2026][Delivery Plan] - January 19th QED Release"),
        new[]
        {
            Ev(Milestone.StartDev, 2026, 1, 5),
            Ev(Milestone.EndDev, 2026, 1, 12),
            Ev(Milestone.QaCutoff, 2026, 1, 14),
            Ev(Milestone.QedDeploy, 2026, 1, 19),
        });

    [Fact]
    public void Render_FullPlan_ShowsReleasePhase()
    {
        var html = new HtmlEmailRenderer().Render(FullPlan(), new LocalDate(2026, 7, 1));

        Assert.Contains("RELEASE", html);
        Assert.Contains("AMER/UK", html);
        Assert.Contains("days to release", html);
        Assert.Contains("August release", html);
    }

    [Fact]
    public void Render_QedOnlyPlan_DoesNotThrow_AndOmitsReleasePhase()
    {
        var html = new HtmlEmailRenderer().Render(QedOnlyPlan(), new LocalDate(2026, 1, 1));

        // The goal falls back to QED deployment instead of a PROD release.
        Assert.Contains("QED deployment", html);
        Assert.Contains("days to QED deploy", html);
        Assert.Contains("DEPLOYMENT", html);

        // No PROD release box and no regression rows for this plan type.
        Assert.DoesNotContain("AMER/UK", html);
        Assert.DoesNotContain("Regression", html);
    }
}
