namespace Core.Domain;

// A delivery plan is built around a SINGLE calendar anchor — the one real date the
// planners commit to for that month. Everything else is derived from it:
//   Prod    -> Anchor = the AMER/UK production Release date. The QED deploy and every
//              other milestone are computed backward from the Release.
//   QedOnly -> Anchor = the QED deploy Monday (busy-season months with no Release).
// This keeps the engine single-parameter: give it one date + the plan kind and it
// produces the whole plan.
public record ReleaseSchedule(
    PlanKind Kind,
    LocalDate Anchor,
    string PlanName = "");
