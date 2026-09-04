namespace Adapters;

// Adapter: translates a domain DeliveryPlan into the Azure DevOps
// "deliveryTimelineView" REST shape and POSTs it to _apis/work/plans.
//
// Symmetric with AdoDeliveryPlanReader / AdoDeliveryPlanCatalog: it receives a
// ready-made AdoConfig by constructor injection and is agnostic of the config
// source (appsettings / user-secrets / env / Key Vault). It only uses the
// AdoConfig helpers (PlansUrl, PlanUrl, AuthHeader) and config.TeamIds.
//
// Marker labels/colors were reverse-engineered from a REAL plan (April 2026)
// and are part of the ADO translation, so they stay here (not config).
public class AdoDeliveryPlanWriter(HttpClient http, AdoConfig config) : IDeliveryPlanWriter
{
    private readonly HttpClient _http = http;
    private readonly AdoConfig _config = config;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Milestone -> (exact ADO marker label, marker color). VERIFIED against the
    // April 2026 plan. Note the intentional "Start- Development" spacing: it is the
    // literal string ADO stores, so we preserve it rather than "correcting" it.
    private static readonly IReadOnlyDictionary<Milestone, (string Label, string Color)> Markers =
        new Dictionary<Milestone, (string, string)>
        {
            [Milestone.StartDev] = ("Start- Development", "#71338D"),
            [Milestone.EndDev] = ("End - Development", "#EF33A3"),
            [Milestone.QaCutoff] = ("QA - Cut-off", "#E87025"),
            [Milestone.QedDeploy] = ("QED - Deployment", "#60AF49"),
            [Milestone.StartReg] = ("Start - Regression testing", "#FBD144"),
            [Milestone.EndReg] = ("End - Regression testing", "#43B4D5"),
            [Milestone.Release] = ("AMER/UK - Release date", "#1B478B"),
        };

    // Part of the ADO body shape, not a deployment setting -> stays in code.
    private const string CategoryReferenceName = "Microsoft.RequirementCategory";

    public async Task<PublishedPlanRef> CreateAsync(
        DeliveryPlan plan,
        PlanPublishOptions options,
        CancellationToken cancellationToken = default)
    {
        // 1) Create the plan. IMPORTANT: Azure DevOps' plan *create* endpoint
        //    persists teams/criteria/cardSettings but SILENTLY DROPS the markers
        //    array, so the plan is born with an empty "Markers" tab. Markers only
        //    stick through a follow-up *update* (PUT) — see UpdateMarkersAsync.
        var createBody = BuildBody(plan, options, revision: null);

        using var req = new HttpRequestMessage(HttpMethod.Post, _config.PlansUrl())
        {
            Content = JsonContent.Create(createBody, options: Json)
        };
        req.Headers.Authorization = _config.AuthHeader();

        using var res = await _http.SendAsync(req, cancellationToken);
        var payload = await res.Content.ReadAsStringAsync(cancellationToken);

        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"ADO plan create failed ({(int)res.StatusCode} {res.StatusCode}): {payload}");

        string planId, planName;
        using (var doc = JsonDocument.Parse(payload))
        {
            var root = doc.RootElement;
            planId = root.GetProperty("id").GetString()!;
            planName = root.GetProperty("name").GetString()!;
        }

        // 2) Persist the markers via an update. Without this the Markers tab stays
        //    empty. Skipped only when there is nothing to paint.
        if (plan.Events.Count > 0)
            await UpdateMarkersAsync(planId, plan, options, cancellationToken);

        return new PublishedPlanRef(planId, planName);
    }

    // Re-sends the full plan body via PUT so the markers actually persist.
    // ADO's update uses optimistic concurrency, so it needs the CURRENT revision.
    // Right after creation two transient errors can occur — 403 (the plan ACL is
    // still propagating) and 400 (a revision race) — so we re-read the revision
    // and retry a few times with a short backoff.
    private async Task UpdateMarkersAsync(
        string planId,
        DeliveryPlan plan,
        PlanPublishOptions options,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 6;

        for (var attempt = 1; ; attempt++)
        {
            var revision = await GetRevisionAsync(planId, cancellationToken);
            var body = BuildBody(plan, options, revision);

            using var req = new HttpRequestMessage(HttpMethod.Put, _config.PlanUrl(planId))
            {
                Content = JsonContent.Create(body, options: Json)
            };
            req.Headers.Authorization = _config.AuthHeader();

            using var res = await _http.SendAsync(req, cancellationToken);
            if (res.IsSuccessStatusCode)
                return;

            var payload = await res.Content.ReadAsStringAsync(cancellationToken);
            var code = (int)res.StatusCode;

            // 403 = ACL still settling after create; 400 = revision mismatch.
            // Both are transient right after creation → back off and retry.
            var transient = code is 400 or 403;
            if (!transient || attempt >= maxAttempts)
                throw new InvalidOperationException(
                    $"ADO marker update failed ({code} {res.StatusCode}) " +
                    $"after {attempt} attempt(s): {payload}");

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

    private async Task<int> GetRevisionAsync(string planId, CancellationToken cancellationToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, _config.PlanUrl(planId));
        req.Headers.Authorization = _config.AuthHeader();

        using var res = await _http.SendAsync(req, cancellationToken);
        var payload = await res.Content.ReadAsStringAsync(cancellationToken);

        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"ADO plan read failed ({(int)res.StatusCode} {res.StatusCode}): {payload}");

        using var doc = JsonDocument.Parse(payload);
        return doc.RootElement.GetProperty("revision").GetInt32();
    }

    public async Task DeleteAsync(string planId, CancellationToken cancellationToken = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete, _config.PlanUrl(planId));
        req.Headers.Authorization = _config.AuthHeader();

        using var res = await _http.SendAsync(req, cancellationToken);
        if (!res.IsSuccessStatusCode)
        {
            var payload = await res.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"ADO plan delete failed ({(int)res.StatusCode} {res.StatusCode}): {payload}");
        }
    }

    // Surgically moves a single marker on an existing plan. Reads the plan's RAW
    // JSON, changes only the target marker's date, and PUTs the whole document
    // back — so revision, teams, cardSettings and even markers the domain reader
    // does not track (e.g. "Communicate Release Plan") are preserved. Uses the
    // same 400/403 transient retry as UpdateMarkersAsync (revision race / ACL).
    public async Task<MarkerUpdateResult> UpdateMarkerDateAsync(
        string planId,
        Milestone marker,
        LocalDate newDate,
        CancellationToken cancellationToken = default)
    {
        const int maxAttempts = 6;

        for (var attempt = 1; ; attempt++)
        {
            // GET the current raw plan (also gives us the revision for the PUT).
            using var getReq = new HttpRequestMessage(HttpMethod.Get, _config.PlanUrl(planId));
            getReq.Headers.Authorization = _config.AuthHeader();

            using var getRes = await _http.SendAsync(getReq, cancellationToken);
            var getPayload = await getRes.Content.ReadAsStringAsync(cancellationToken);
            if (!getRes.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"ADO plan read failed ({(int)getRes.StatusCode} {getRes.StatusCode}): {getPayload}");

            var (updatedJson, found, previous, count) = ApplyMarkerDate(getPayload, marker, newDate);
            if (!found)
                return new MarkerUpdateResult(false, null, 0);

            // PUT the mutated document back verbatim (revision travels inside it).
            using var putReq = new HttpRequestMessage(HttpMethod.Put, _config.PlanUrl(planId))
            {
                Content = new StringContent(updatedJson, Encoding.UTF8, "application/json")
            };
            putReq.Headers.Authorization = _config.AuthHeader();

            using var putRes = await _http.SendAsync(putReq, cancellationToken);
            if (putRes.IsSuccessStatusCode)
                return new MarkerUpdateResult(true, previous, count);

            var putPayload = await putRes.Content.ReadAsStringAsync(cancellationToken);
            var code = (int)putRes.StatusCode;

            // 400 = revision race; 403 = ACL still settling. Both transient → retry.
            var transient = code is 400 or 403;
            if (!transient || attempt >= maxAttempts)
                throw new InvalidOperationException(
                    $"ADO marker update failed ({code} {putRes.StatusCode}) " +
                    $"after {attempt} attempt(s): {putPayload}");

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

    // Pure, network-free JSON mutation: finds the marker whose ADO label maps to
    // the requested Milestone (tolerant match, via AdoDeliveryPlanReader.MapLabel),
    // rewrites its date to UTC midnight, and returns the modified JSON, whether a
    // match was found, the previous date, and how many markers changed. Every
    // other field/marker in the document is preserved untouched.
    public static (string Json, bool Found, LocalDate? PreviousDate, int Count) ApplyMarkerDate(
        string planJson, Milestone marker, LocalDate newDate)
    {
        var root = JsonNode.Parse(planJson)!.AsObject();

        if (root["properties"] is not JsonObject props ||
            props["markers"] is not JsonArray markers)
            return (planJson, false, null, 0);

        LocalDate? previous = null;
        var count = 0;

        foreach (var node in markers)
        {
            if (node is not JsonObject m) continue;

            var label = m["label"]?.GetValue<string>();
            if (label is null || AdoDeliveryPlanReader.MapLabel(label) != marker)
                continue;

            if (previous is null &&
                m["date"]?.GetValue<string>() is { Length: > 0 } raw &&
                TryParseAdoDate(raw, out var prev))
                previous = prev;

            m["date"] = IsoUtcMidnight(newDate);
            count++;
        }

        return count == 0
            ? (planJson, false, null, 0)
            : (root.ToJsonString(), true, previous, count);
    }

    private static bool TryParseAdoDate(string raw, out LocalDate date)
    {
        var parsed = InstantPattern.ExtendedIso.Parse(raw);
        if (parsed.Success)
        {
            date = parsed.Value.InUtc().Date;
            return true;
        }
        date = default;
        return false;
    }

    // ---- body construction ------------------------------------------------

    private object BuildBody(DeliveryPlan plan, PlanPublishOptions options, int? revision)
    {
        // Teams come from config (AdoConfig.TeamIds) -> no hardcoded GUIDs.
        var teamBacklogMappings = _config.TeamIds
            .Select(id => new
            {
                teamId = id,
                categoryReferenceName = CategoryReferenceName
            })
            .ToArray();

        // Each tag becomes an AND "Tags CONTAINS <value>" criterion. The first has
        // no index; the rest are indexed 1..n, matching the real plan's shape.
        var criteria = options.Tags
            .Select((value, i) =>
            {
                var c = new Dictionary<string, object>
                {
                    ["fieldName"] = "System.Tags",
                    ["logicalOperator"] = "AND",
                    ["operator"] = "CONTAINS",
                    ["value"] = value
                };
                if (i > 0) c["index"] = i;
                return c;
            })
            .ToArray();

        var markers = plan.Events
            .Select(e =>
            {
                var (label, color) = Markers[e.Label];
                return new
                {
                    date = IsoUtcMidnight(e.Date),
                    label,
                    color
                };
            })
            .ToArray();

        var properties = new
        {
            teamBacklogMappings,
            criteria,
            cardSettings = DefaultCardSettings,
            markers,
            styleSettings = Array.Empty<object>(),
            tagStyleSettings = Array.Empty<object>()
        };

        var body = new Dictionary<string, object>
        {
            ["name"] = options.Name,
            ["type"] = "deliveryTimelineView",
            ["properties"] = properties
        };

        // Update (PUT) requires the current revision for optimistic concurrency.
        // Create (POST) must NOT send it.
        if (revision is not null)
            body["revision"] = revision.Value;

        return body;
    }

    // Card settings copied verbatim from the real April 2026 plan.
    private static readonly object DefaultCardSettings = new
    {
        fields = new
        {
            showId = false,
            showAssignedTo = true,
            assignedToDisplayFormat = "avatarOnly",
            showState = true,
            showTags = true,
            showParent = false,
            showEmptyFields = false,
            showChildRollup = false,
            additionalFields = (object?)null,
            coreFields = new object[]
            {
                new { referenceName = "System.AssignedTo", displayName = "Assigned To", fieldType = "string", isIdentity = true },
                new { referenceName = "System.State",      displayName = "State",       fieldType = "string", isIdentity = false },
                new { referenceName = "System.Tags",       displayName = "Tags",        fieldType = "plainText", isIdentity = false }
            }
        }
    };

    // ---- helpers ----------------------------------------------------------

    // NodaTime LocalDate -> "yyyy-MM-ddT00:00:00Z". Built by parts to avoid any
    // pattern ambiguity between NodaTime and BCL format specifiers.
    private static string IsoUtcMidnight(NodaTime.LocalDate d) =>
        $"{d.Year:D4}-{d.Month:D2}-{d.Day:D2}T00:00:00Z";
}