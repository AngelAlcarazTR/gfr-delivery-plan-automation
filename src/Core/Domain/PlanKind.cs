namespace Core.Domain;

// Distinguishes the two cadences observed in the GFR delivery plans:
//   Prod    -> full cycle ending in an AMER/UK production Release (7 markers).
//   QedOnly -> busy-season cycle that stops at the QED deploy (no regression/release).
public enum PlanKind
{
    Prod,
    QedOnly
}
