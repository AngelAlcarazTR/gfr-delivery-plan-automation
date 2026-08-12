namespace Core.Domain;

// The anchors a delivery plan is built around. Both come straight from the
// official GoFileRoom production release calendar:
//   QedDeploy -> the QED deploy Monday (present in every plan).
//   Release   -> the AMER/UK production release date (Prod plans only).
// Every other milestone is derived backward from these anchors.
public record ReleaseSchedule(
    PlanKind Kind,
    LocalDate QedDeploy,
    LocalDate? Release = null,
    string PlanName = "");
