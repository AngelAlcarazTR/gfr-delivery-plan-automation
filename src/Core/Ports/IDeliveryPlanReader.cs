namespace Core.Ports;

public interface IDeliveryPlanReader
{
    Task<DeliveryPlan> GetPlanAsync(string planId, CancellationToken ct = default);
}
