namespace Core.Domain;

public record DeliveryPlan(
    Sprint Sprint,
    IReadOnlyList<PlanEvent> Events,
    // Source identifiers, populated when the plan is read from ADO. Used to build
    // deep-links in the email (plan timeline + per-release ticket query). Null for
    // computed/POC plans that were never persisted.
    string? PlanId = null,
    IReadOnlyList<string>? Tags = null);