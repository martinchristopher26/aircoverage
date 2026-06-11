namespace AirCoverage.Api.Ado;

/// <summary>A work item flattened to the fields we use.</summary>
public record AdoWorkItem(
    int Id,
    string Title,
    string Description,
    int? Priority,
    string State,
    string? AssignedToDisplayName,
    string Tags,
    string Url,
    DateTime CreatedDate,
    DateTime? ChangedDate);

/// <summary>One operation in a JSON-Patch document sent to ADO.</summary>
public record JsonPatchOperation(string op, string path, object? value);
