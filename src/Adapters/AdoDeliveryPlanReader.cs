namespace Adapters;

// Reads a Delivery Plan's markers from Azure DevOps and maps them into the
// domain DeliveryPlan so the existing renderer can consume it unchanged.
public class AdoDeliveryPlanReader(HttpClient http, AdoConfig config) : IDeliveryPlanReader
{
    private readonly HttpClient _http = http;
    private readonly AdoConfig _config = config;

    public async Task<DeliveryPlan> GetPlanAsync(string planId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _config.PlanUrl(planId));
        request.Headers.Authorization = _config.AuthHeader();

        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        return ParsePlan(json);
    }

    // Pure, network-free parser — mirrors AdoSprintReader.ParseCurrentSprint so it can be unit tested.
    public static DeliveryPlan ParsePlan(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var planName = root.GetProperty("name").GetString() ?? "";
        var planId = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;

        var events = new List<PlanEvent>();
        var seen = new HashSet<Milestone>();
        List<string>? tags = null;

        if (root.TryGetProperty("properties", out var props))
        {
            if (props.TryGetProperty("markers", out var markers) &&
                markers.ValueKind == JsonValueKind.Array)
            {
                foreach (var marker in markers.EnumerateArray())
                {
                    var label = marker.GetProperty("label").GetString() ?? "";
                    var milestone = MapLabel(label);
                    if (milestone is null || !seen.Add(milestone.Value))
                        continue; // unknown label or duplicate milestone — skip

                    var date = ParseAdoDate(marker.GetProperty("date").GetString()!);
                    events.Add(new PlanEvent(milestone.Value, date, false, null));
                }
            }

            // The plan's tag filter(s) drive which tickets belong to this release.
            // We capture them so the email can deep-link to a live status query.
            if (props.TryGetProperty("criteria", out var criteria) &&
                criteria.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in criteria.EnumerateArray())
                {
                    if (c.TryGetProperty("fieldName", out var fn) &&
                        string.Equals(fn.GetString(), "System.Tags", StringComparison.OrdinalIgnoreCase) &&
                        c.TryGetProperty("value", out var v) &&
                        v.GetString() is { Length: > 0 } tag)
                    {
                        (tags ??= new()).Add(tag);
                    }
                }
            }
        }

        // Chronological order — the renderer relies on it for "next milestone".
        events.Sort((a, b) => a.Date.CompareTo(b.Date));

        var startDate = events.Count > 0 ? events[0].Date : default;
        var sprint = new Sprint(startDate, planName);

        return new DeliveryPlan(sprint, events, planId, tags);
    }

    // Maps the free-text ADO marker label to a domain Milestone.
    // Tolerant to spacing/casing variants across plan versions; returns null for
    // markers we don't track (e.g. "Communicate Release Plan", "QA - Approval").
    public static Milestone? MapLabel(string rawLabel)
    {
        var l = rawLabel.ToLowerInvariant();

        var hasStart = l.Contains("start");
        var hasEnd = l.Contains("end");

        if (l.Contains("regression"))
        {
            if (hasStart) return Milestone.StartReg;
            if (hasEnd) return Milestone.EndReg;
            return null;
        }

        if (l.Contains("develop"))
        {
            if (hasStart) return Milestone.StartDev;
            if (hasEnd) return Milestone.EndDev;
            return null;
        }

        if (l.Contains("qed") && l.Contains("deploy"))
            return Milestone.QedDeploy;

        if (l.Contains("qa") && l.Contains("cut"))
            return Milestone.QaCutoff;

        if (l.Contains("release") && (l.Contains("amer") || l.Contains("uk")))
            return Milestone.Release;

        return null;
    }

    private static LocalDate ParseAdoDate(string raw)
    {
        // ADO stores markers at midnight UTC ("2026-08-24T00:00:00Z").
        // Stay in UTC — converting to local time shifts the calendar day.
        var instant = InstantPattern.General.Parse(raw).Value;
        return instant.InUtc().Date;
    }
}
