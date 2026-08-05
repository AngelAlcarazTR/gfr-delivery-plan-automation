namespace Adapters;

public class GraphDraftCreator : IDraftCreator
{
    private readonly GraphServiceClient _graph;

    public GraphDraftCreator(GraphConfig config)
    {
        var options = new InteractiveBrowserCredentialOptions
        {
            TenantId = config.TenantId,
            ClientId = config.ClientId,
            RedirectUri = new Uri("http://localhost")
        };

        var credential = new InteractiveBrowserCredential(options);
        _graph = new GraphServiceClient(credential, ["Mail.ReadWrite"]);
    }

    public async Task CreateDraftAsync(string subject, string htmlBody, CancellationToken ct = default)
    {
        var message = new Message
        {
            Subject = subject,
            Body = new ItemBody
            {
                ContentType = BodyType.Html,
                Content = htmlBody
            }
        };

        // POST a /me/messages creates a draft message in the signed-in user's mailbox
        await _graph.Me.Messages.PostAsync(message, cancellationToken: ct);
    }
}