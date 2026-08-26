namespace Adapters;

// Builds the developer-facing deep-links shown in the delivery-plan email.
// These are ADO-specific, so they live in the adapter layer (not in Core).
//
// Both links are per-release, so a developer who opens the email lands exactly
// on THIS release's plan and THIS release's tickets — not a generic dashboard.
public static class AdoPlanLinks
{
    // Opens the plan's own timeline in Azure Boards → Delivery Plans.
    // Stable, well-known route: .../_deliveryplans/plan/{planId}.
    public static string DeliveryPlanUrl(AdoConfig ado, string planId) =>
        $"{Root(ado)}/_deliveryplans/plan/{planId}";

    // Opens a LIVE work-item query filtered by the release tag(s), so the reader
    // sees the current status (state + assignee) of exactly this release's tickets.
    // The WIQL is embedded in the URL, so no saved/temporary query has to exist.
    public static string TicketStatusQueryUrl(AdoConfig ado, IReadOnlyList<string> tags)
    {
        var tagClause = string.Join(
            " OR ",
            tags.Select(t => $"[System.Tags] CONTAINS '{EscapeWiql(t)}'"));

        var wiql =
            "SELECT [System.Id],[System.WorkItemType],[System.Title],[System.State],[System.AssignedTo] " +
            "FROM WorkItems " +
            $"WHERE [System.TeamProject] = @project AND ({tagClause}) " +
            "ORDER BY [System.State] ASC,[System.AssignedTo] ASC";

        return $"{Root(ado)}/_queries/query/?wiql={Uri.EscapeDataString(wiql)}";
    }

    private static string Root(AdoConfig ado) =>
        $"{ado.BaseUrl.TrimEnd('/')}/{ado.Organization}/{ado.Project}";

    // WIQL string literals escape a single quote by doubling it.
    private static string EscapeWiql(string value) => value.Replace("'", "''");
}
