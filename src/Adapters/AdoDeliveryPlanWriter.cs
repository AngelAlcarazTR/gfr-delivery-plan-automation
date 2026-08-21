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
        var body = BuildBody(plan, options);

        using var req = new HttpRequestMessage(HttpMethod.Post, _config.PlansUrl())
        {
            Content = JsonContent.Create(body, options: Json)
        };
        req.Headers.Authorization = _config.AuthHeader();

        using var res = await _http.SendAsync(req, cancellationToken);
        var payload = await res.Content.ReadAsStringAsync(cancellationToken);

        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"ADO plan create failed ({(int)res.StatusCode} {res.StatusCode}): {payload}");

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        return new PublishedPlanRef(
            root.GetProperty("id").GetString()!,
            root.GetProperty("name").GetString()!);
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

    // ---- body construction ------------------------------------------------

    private object BuildBody(DeliveryPlan plan, PlanPublishOptions options)
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

        return new
        {
            name = options.Name,
            type = "deliveryTimelineView",
            properties = new
            {
                teamBacklogMappings,
                criteria,
                cardSettings = DefaultCardSettings,
                markers,
                styleSettings = Array.Empty<object>(),
                tagStyleSettings = Array.Empty<object>()
            }
        };
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