namespace Adapters;

// Presentation-only branding for the email card. Externalized so the sender
// identity and links are configuration, not hardcoded to a single person/team.
public record EmailBranding(
    string SenderName,
    string SenderTitle,
    string DashboardUrl,
    string TicketStatusUrl)
{
    // Neutral fallback used when no configuration is supplied (e.g. unit tests).
    public static EmailBranding Default { get; } = new(
        SenderName: "GFR Delivery Plan",
        SenderTitle: "Thomson Reuters · GoFileRoom",
        DashboardUrl: "#",
        TicketStatusUrl: "#");
}
