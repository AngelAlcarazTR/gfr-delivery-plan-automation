namespace Core.Application;

// Everything the writer needs that is NOT already inside the DeliveryPlan itself,
// but is still domain-level (no Azure concepts here). ADO-specific details
// (team GUIDs, marker colors, card settings) live in the adapter, not here.
public sealed record PlanPublishOptions(
    // The plan's display name, e.g. "[GFR][2026][Delivery Plan] - September 14th QED".
    // Callers own the naming convention; the writer does not invent it.
    string Name,

    // The System.Tags values used to scope which work items appear on the plan,
    // e.g. ["2026.09", "2026.09_QED"]. Each becomes a "Tags CONTAINS <value>" criterion.
    // Empty => a plan with no criteria (renders empty; useful for smoke tests).
    IReadOnlyList<string> Tags);

// Lightweight handle to a plan that exists in the backing system.
public sealed record PublishedPlanRef(string Id, string Name);