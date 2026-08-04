namespace Core.Ports;

public interface ISprintReader
{
    Task<Sprint> GetCurrentSprintAsync(CancellationToken ct = default);
}