namespace Functions;

// ---- Request ----------------------------------------------------------------

// Body of POST /api/plan-year/compute.
// Anchors come in the body (no calendar reader needed) so this endpoint can be
// used for backtesting: feed the 2025/2026 anchors and inspect what the engine
// computes, without touching ADO.
public sealed record PlanYearComputeRequest(
    int Year,
    IReadOnlyList<AnchorInput> Anchors);

// One month's anchors. Dates are ISO strings ("2026-04-13") to avoid ambiguity.
// NOTE: there is NO "kind" field. Whether a month is Prod or QedOnly is a fixed
// business rule (busy season = QED-only), inferred by the endpoint from the month.
// Release is provided only for Prod months; it must be null/absent for busy months.
public sealed record AnchorInput(
    int Month,
    string Qed,
    string? Release = null);

// ---- Response ---------------------------------------------------------------

public sealed record PlanYearComputeResponse(
    int Year,
    int Count,
    IReadOnlyList<ComputedPlan> Plans,
    IReadOnlyList<ComputeError> Errors);

public sealed record ComputedPlan(
    int Month,
    string Kind,
    string PlanName,
    IReadOnlyList<ComputedMarker> Markers);

public sealed record ComputedMarker(
    string Label,
    string Date);   // ISO "yyyy-MM-dd"

public sealed record ComputeError(
    int Month,
    string Reason);