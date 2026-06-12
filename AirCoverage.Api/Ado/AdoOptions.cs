namespace AirCoverage.Api.Ado;

public class AdoOptions
{
    public const string Section = "Ado";

    public string OrgUrl { get; set; } = "https://dev.azure.com/JustFOIA";
    public string Project { get; set; } = "JustFOIA Core";
    // Provide via secret (env Ado__Pat / Docker secret / user-secrets) — never appsettings.json.
    public string Pat { get; set; } = "";
    public string Tag { get; set; } = "AirCoverage";
    public string? AreaPath { get; set; }
    public string WorkItemType { get; set; } = "Bug";
    // Only these work-item types are admitted to the queue (sync cache + closed scope).
    // Any other type (Epic, Feature, Task, …) is excluded even if it satisfies membership.
    public string[] IncludedTypes { get; set; } = new[] { "User Story", "Bug" };
    public int PollSeconds { get; set; } = 60;
    public int ClosedWindowDays { get; set; } = 30;
    // An item also belongs to the queue if it is a descendant (any depth) of this
    // epic work item; set to 0 to disable the subtree membership source.
    public int ParentWorkItemId { get; set; } = 22691;
    // ADO WIQL is eventually consistent: a just-written item can be absent from the
    // membership query for a few seconds. Cache rows written locally (write-through)
    // within this window are protected from the full-resync prune.
    public int IndexingGraceSeconds { get; set; } = 120;
}
