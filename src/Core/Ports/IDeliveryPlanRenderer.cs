namespace Core.Ports;

public interface IDeliveryPlanRenderer
{
    string Render(DeliveryPlan plan, LocalDate today);
}