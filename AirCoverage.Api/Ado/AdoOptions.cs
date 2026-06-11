namespace AirCoverage.Api.Ado;

public class AdoOptions
{
    public const string Section = "Ado";

    public string OrgUrl { get; set; } = "";          // https://dev.azure.com/yourorg
    public string Project { get; set; } = "";
    // Provide via secret (env Ado__Pat / Docker secret / user-secrets) — never appsettings.json.
    public string Pat { get; set; } = "";
    public string Tag { get; set; } = "AirCoverage";
    public string? AreaPath { get; set; }
    public string WorkItemType { get; set; } = "Bug";
    public int PollSeconds { get; set; } = 60;
    public int ReconcileSeconds { get; set; } = 300;
    public int ClosedWindowDays { get; set; } = 30;
    // Re-query a little before the last watermark to cover ADO indexing lag / clock
    // skew so a change committed near the previous query instant isn't missed.
    public int WatermarkOverlapSeconds { get; set; } = 120;
}
