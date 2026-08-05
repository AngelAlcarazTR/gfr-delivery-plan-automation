namespace Core.Ports;

public interface IDraftCreator
{
    Task CreateDraftAsync(string subject, string htmlBody, CancellationToken ct = default);
}