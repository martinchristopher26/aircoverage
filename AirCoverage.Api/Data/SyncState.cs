namespace AirCoverage.Api.Data;

public class SyncState
{
    public int Id { get; set; }                 // singleton row, Id = 1
    public DateTime? LastChangedWatermark { get; set; }
    public DateTime? LastSuccessfulSync { get; set; }
}
