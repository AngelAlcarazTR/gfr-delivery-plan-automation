namespace Core.Domain;

public static class CurrentPlanSelector
{
    // Picks the plan whose goal date is the nearest >= today.
    // Falls back to the most recent past plan. Ignores plans with no parseable date.
    public static DeliveryPlanRef? Pick(IEnumerable<DeliveryPlanRef> plans, LocalDate today)
    {
        var dated = plans.Where(p => p.GoalDate is not null).ToList();

        var future = dated.Where(p => p.GoalDate!.Value >= today)
                          .OrderBy(p => p.GoalDate!.Value)
                          .FirstOrDefault();
        if (future is not null) return future;

        // Fallback: latest past plan
        return dated.OrderByDescending(p => p.GoalDate!.Value).FirstOrDefault();
    }
}