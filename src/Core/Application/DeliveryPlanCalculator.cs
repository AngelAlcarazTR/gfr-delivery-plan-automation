namespace Core.Application;

public static class DeliveryPlanCalculator
{
    public static DeliveryPlan Compute(Sprint sprint, EngineConfig config)
    {
        var startDev = sprint.StartDate;
        var endDev = BusinessDayCalculator.AddBusinessDays(startDev, config.DevelopmentDays - 1);
        var qed = BusinessDayCalculator.AddBusinessDays(endDev, config.QedGapDays);
        var startReg = BusinessDayCalculator.AddBusinessDays(qed, config.RegressionGapDays);
        var endReg = BusinessDayCalculator.AddBusinessDays(startReg, config.RegressionDays - 1);
        var release = FirstBusinessMondayAfter(endReg);

        var events = new List<PlanEvent>
        {
            new(Milestone.StartDev,  startDev,  false, null),
            new(Milestone.EndDev,    endDev,    false, null),
            new(Milestone.QedDeploy, qed,       false, null),
            new(Milestone.StartReg,  startReg,  false, null),
            new(Milestone.EndReg,    endReg,    false, null),
            new(Milestone.Release,   release,   false, null),
        };

        return new DeliveryPlan(sprint, events);
    }

    private static LocalDate FirstBusinessMondayAfter(LocalDate date)
    {
        var next = date.PlusDays(1);
        while (next.DayOfWeek != IsoDayOfWeek.Monday)
        {
            next = next.PlusDays(1);
        }
        return next;
    }
}
