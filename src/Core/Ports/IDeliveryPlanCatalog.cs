namespace Core.Ports;

// Finds Delivery Plans by a free-text filter (matches plan name or owner),
// mirroring the Azure DevOps "planTextFilter" box. The filter is a parameter,
// not hardcoded, so the same component scales to other owners/teams later.
public interface IDeliveryPlanCatalog
{
    Task<IReadOnlyList<DeliveryPlanRef>> FindPlansAsync(
        string textFilter, CancellationToken ct = default);
}
