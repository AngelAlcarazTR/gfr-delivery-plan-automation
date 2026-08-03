namespace Core.Domain;

public record DeliveryPlan(
    Sprint Sprint,
    IReadOnlyList<PlanEvent> Events);