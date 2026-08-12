namespace Core.Domain;

// Lightweight summary of a Delivery Plan (from the plans list), enough to pick
// which plan to read markers from. Not the full plan.
public record DeliveryPlanRef(
    string Id,
    string Name,
    string Owner,
    Instant ModifiedAt);
