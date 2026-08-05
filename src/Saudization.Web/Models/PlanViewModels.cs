namespace Saudization.Web.Models;

/// <summary>One edited (category × registration) count posted from the enhanced grid.</summary>
public class PostedCell
{
    public int RecordId { get; set; }
    public int? CategoryId { get; set; }   // null = Unclassified row
    public int Saudi { get; set; }
    public int NonSaudi { get; set; }
}

/// <summary>Body of POST /Saudization/SavePlan.</summary>
public class SavePlanPayload
{
    public int SnapshotId { get; set; }
    public string? Label { get; set; }
    public List<PostedCell> Cells { get; set; } = new();
}

/// <summary>Body of POST /Saudization/Export (original recomputed from snapshot, enhanced from these cells).</summary>
public class ExportPayload
{
    public int SnapshotId { get; set; }
    public string Mode { get; set; } = "current";
    public List<PostedCell> Cells { get; set; } = new();
}
