namespace Adapters;

// Central Azure DevOps connection settings. BaseUrl/ApiVersion are externalized
// so the same code works against other orgs or on-prem ADO without recompiling.
public record AdoConfig(
    string Organization,
    string Project,
    string Team,
    string Pat,
    IReadOnlyList<string> TeamIds,
    string BaseUrl = "https://dev.azure.com",
    string ApiVersion = "7.1")
{
    private string ProjectRoot => $"{BaseUrl.TrimEnd('/')}/{Organization}/{Project}";

    public string PlansUrl() =>
        $"{ProjectRoot}/_apis/work/plans?api-version={ApiVersion}";

    public string PlanUrl(string planId) =>
        $"{ProjectRoot}/_apis/work/plans/{planId}?api-version={ApiVersion}";

    public string CurrentIterationsUrl() =>
        $"{ProjectRoot}/{Team}/_apis/work/teamsettings/iterations?$timeframe=current&api-version={ApiVersion}";

    // Basic auth header for a PAT: base64(":{pat}"). Kept here so no adapter
    // rebuilds (or accidentally logs) the credential.
    public AuthenticationHeaderValue AuthHeader()
    {
        var b64 = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{Pat}"));
        return new AuthenticationHeaderValue("Basic", b64);
    }
}