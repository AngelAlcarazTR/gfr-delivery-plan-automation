namespace Adapters;

// Lists Delivery Plans from Azure DevOps and filters them by a free-text term
// that matches the plan name or its owner (created/modified by), replicating
// the ADO "planTextFilter" search box.
public class AdoDeliveryPlanCatalog(HttpClient http, AdoConfig config) : IDeliveryPlanCatalog
{
    private readonly HttpClient _http = http;
    private readonly AdoConfig _config = config;

    public async Task<IReadOnlyList<DeliveryPlanRef>> FindPlansAsync(
        string textFilter, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _config.PlansUrl());
        request.Headers.Authorization = _config.AuthHeader();

        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        return ParsePlanList(json, textFilter);
    }

    // Pure, network-free parser + filter — unit testable like AdoSprintReader.
    public static IReadOnlyList<DeliveryPlanRef> ParsePlanList(string json, string textFilter)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var result = new List<DeliveryPlanRef>();
        if (!root.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var p in value.EnumerateArray())
        {
            var name = GetString(p, "name");
            var owner = GetIdentityName(p, "createdByIdentity");
            var modifiedBy = GetIdentityName(p, "modifiedByIdentity");

            if (!Matches(textFilter, name, owner, modifiedBy))
                continue;

            var id = GetString(p, "id");
            var modifiedAt = ParseInstant(GetString(p, "modifiedDate"));

            // Prefer the creator as the displayed owner; fall back to modifier.
            var displayOwner = string.IsNullOrWhiteSpace(owner) ? modifiedBy : owner;
            result.Add(new DeliveryPlanRef(id, name, displayOwner, modifiedAt));
        }

        // Most recently modified first — a good proxy for "the current plan".
        result.Sort((a, b) => b.ModifiedAt.CompareTo(a.ModifiedAt));
        return result;
    }

    // Matches the ADO search box: text found in the plan name OR the owner
    // (created-by or modified-by) display name. Empty filter matches everything.
    public static bool Matches(string textFilter, string name, string createdBy, string modifiedBy)
    {
        if (string.IsNullOrWhiteSpace(textFilter))
            return true;

        var t = textFilter.Trim();
        return Contains(name, t) || Contains(createdBy, t) || Contains(modifiedBy, t);
    }

    private static bool Contains(string? haystack, string needle) =>
        haystack is not null &&
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static string GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";

    private static string GetIdentityName(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var id) && id.ValueKind == JsonValueKind.Object
            ? GetString(id, "displayName")
            : "";

    private static Instant ParseInstant(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Instant.MinValue;
        // ADO timestamps may include fractional seconds (e.g. "...:42.333Z"),
        // which ExtendedIso handles (General does not).
        return InstantPattern.ExtendedIso.Parse(raw).Value;
    }
}
