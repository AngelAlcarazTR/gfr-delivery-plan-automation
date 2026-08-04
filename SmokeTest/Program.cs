var config = new AdoConfig(
    Organization: "tr-tax",
    Project: "TaxProf",
    Team: "TaxProf Team",
    Pat: Environment.GetEnvironmentVariable("ADO_PAT") ?? "");

using var http = new HttpClient();
var reader = new AdoSprintReader(http, config);

var sprint = await reader.GetCurrentSprintAsync();

Console.WriteLine($"Sprint actual: {sprint.SprintId}");
Console.WriteLine($"Start date:    {sprint.StartDate}");