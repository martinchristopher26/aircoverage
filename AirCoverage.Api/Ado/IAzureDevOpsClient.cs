namespace AirCoverage.Api.Ado;

public interface IAzureDevOpsClient
{
    Task<IReadOnlyList<int>> QueryOpenIdsAsync(CancellationToken ct);
    Task<IReadOnlyList<int>> QueryIdsChangedSinceAsync(DateTime sinceUtc, CancellationToken ct);
    Task<IReadOnlyList<int>> QueryClosedIdsAsync(int windowDays, CancellationToken ct);
    Task<IReadOnlyList<AdoWorkItem>> GetWorkItemsAsync(IReadOnlyCollection<int> ids, CancellationToken ct);
    Task<AdoWorkItem?> GetWorkItemAsync(int id, CancellationToken ct);
    Task<AdoWorkItem> CreateAsync(IReadOnlyList<JsonPatchOperation> ops, CancellationToken ct);
    Task<AdoWorkItem> UpdateAsync(int id, IReadOnlyList<JsonPatchOperation> ops, CancellationToken ct);
}
