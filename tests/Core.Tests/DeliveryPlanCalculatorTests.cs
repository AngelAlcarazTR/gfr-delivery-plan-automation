namespace Core.Tests;

public class DeliveryPlanCalculatorTests
{
    [Fact]
    public void JulyRelease_GoldenTest_Produces7CorrectDates()
    {
        // Arrange — July release real email, starts Monday 22/06/2026
        var sprint = new Sprint(new LocalDate(2026, 6, 22), "JUL_2026");
        var config = new EngineConfig(
            DevelopmentDays: 12,
            QaCutoffGapDays: 3,
            QedGapDays: 1,
            RegressionGapDays: 0,
            RegressionDays: 7);

        // Act
        var plan = DeliveryPlanCalculator.Compute(sprint, config);

        // Assert - the last 7 exact dates from the real email
        Assert.Equal(new LocalDate(2026, 6, 22), plan.Events[0].Date); // Start Dev
        Assert.Equal(new LocalDate(2026, 7, 7), plan.Events[1].Date); // End Dev
        Assert.Equal(new LocalDate(2026, 7, 10), plan.Events[2].Date); // QA Cut-off
        Assert.Equal(new LocalDate(2026, 7, 13), plan.Events[3].Date); // QED Deploy
        Assert.Equal(new LocalDate(2026, 7, 13), plan.Events[4].Date); // Start Reg (= QED)
        Assert.Equal(new LocalDate(2026, 7, 21), plan.Events[5].Date); // End Reg
        Assert.Equal(new LocalDate(2026, 7, 27), plan.Events[6].Date); // Release
    }

    [Fact]
    public void AugustRelease_GoldenTest_Produces7CorrectDates()
    {
        // Arrange — August release real email, starts Monday 20/07/2026
        var sprint = new Sprint(new LocalDate(2026, 7, 20), "AUG_2026");
        var config = new EngineConfig(
            DevelopmentDays: 12,
            QaCutoffGapDays: 3,
            QedGapDays: 1,
            RegressionGapDays: 0,
            RegressionDays: 7);

        // Act
        var plan = DeliveryPlanCalculator.Compute(sprint, config);

        // Assert — the last 7 exact dates from the real email
        Assert.Equal(new LocalDate(2026, 7, 20), plan.Events[0].Date); // Start Dev
        Assert.Equal(new LocalDate(2026, 8, 4), plan.Events[1].Date); // End Dev
        Assert.Equal(new LocalDate(2026, 8, 7), plan.Events[2].Date); // QA Cut-off
        Assert.Equal(new LocalDate(2026, 8, 10), plan.Events[3].Date); // QED Deploy
        Assert.Equal(new LocalDate(2026, 8, 10), plan.Events[4].Date); // Start Reg (= QED)
        Assert.Equal(new LocalDate(2026, 8, 18), plan.Events[5].Date); // End Reg
        Assert.Equal(new LocalDate(2026, 8, 24), plan.Events[6].Date); // Release
    }

    [Fact]
    public void AnyRelease_AlwaysHolds7Invariants()
    {
        // Arrange — any release, starts Monday 31/08/2026
        var sprint = new Sprint(new LocalDate(2026, 8, 31), "SEP_2026");
        var config = new EngineConfig(
            DevelopmentDays: 12,
            QaCutoffGapDays: 3,
            QedGapDays: 1,
            RegressionGapDays: 0,
            RegressionDays: 7);

        // Act
        var plan = DeliveryPlanCalculator.Compute(sprint, config);
        var e = plan.Events;

        // Assert — invariantes que SIEMPRE deben cumplirse
        Assert.Equal(7, e.Count);                                    // 1. Always 7 milestones
        Assert.Equal(e[3].Date, e[4].Date);                         // 2. Start Reg = QED
        Assert.Equal(IsoDayOfWeek.Monday, e[6].Date.DayOfWeek);     // 3. Release on

        // Cronological order (each date >= previous)
        for (var i = 1; i < e.Count; i++)
            Assert.True(e[i].Date >= e[i - 1].Date);

        // 5. No date falls on weekend
        foreach (var ev in e)
        {
            Assert.NotEqual(IsoDayOfWeek.Saturday, ev.Date.DayOfWeek);
            Assert.NotEqual(IsoDayOfWeek.Sunday, ev.Date.DayOfWeek);
        }
    }
}
