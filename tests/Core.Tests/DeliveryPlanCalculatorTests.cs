namespace Core.Tests;

public class DeliveryPlanCalculatorTests
{
    [Fact]
    public void Sprint10_GoldenTest_Produces6CorrectDates()
    {
        // Arrange — el Sprint 10, empieza miércoles 03/06/2026
        var sprint = new Sprint(new LocalDate(2026, 6, 3), "Sprint-10");
        var config = new EngineConfig(
            DevelopmentDays: 10,
            QedGapDays: 1,
            RegressionGapDays: 1,
            RegressionDays: 5);

        // Act — calcular el plan
        var plan = DeliveryPlanCalculator.Compute(sprint, config);

        // Assert — las 6 fechas exactas del diseño
        Assert.Equal(new LocalDate(2026, 6, 3), plan.Events[0].Date); // Start Dev
        Assert.Equal(new LocalDate(2026, 6, 16), plan.Events[1].Date); // End Dev
        Assert.Equal(new LocalDate(2026, 6, 17), plan.Events[2].Date); // QED Deploy
        Assert.Equal(new LocalDate(2026, 6, 18), plan.Events[3].Date); // Start Reg
        Assert.Equal(new LocalDate(2026, 6, 24), plan.Events[4].Date); // End Reg
        Assert.Equal(new LocalDate(2026, 6, 29), plan.Events[5].Date); // Release
    }
}
