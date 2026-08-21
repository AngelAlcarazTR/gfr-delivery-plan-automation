namespace Core.Application;

// Port: writes a DeliveryPlan out to whatever backing system implements it
// (Azure DevOps today, potentially another tool tomorrow). This interface lives
// in Core and has ZERO references to Azure/HTTP on purpose -- the symmetric twin
// of IDeliveryPlanReader. All translation to ADO's REST shape happens in the adapter.
public interface IDeliveryPlanWriter
{
    // Creates a brand-new Delivery Plan from the domain object.
    // Returns a lightweight reference (id + name) to the created plan.
    Task<PublishedPlanRef> CreateAsync(
        DeliveryPlan plan,
        PlanPublishOptions options,
        CancellationToken cancellationToken = default);

    // Deletes a plan by its backing-system id. Kept here so the same port
    // covers the full create/delete test loop we validated by hand.
    Task DeleteAsync(string planId, CancellationToken cancellationToken = default);
}