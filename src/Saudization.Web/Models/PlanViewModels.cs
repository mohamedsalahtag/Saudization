namespace Saudization.Web.Models;

/// <summary>One edited (category × registration) count posted from the enhanced grid.</summary>
public class PostedCell
{
    public int RecordId { get; set; }
    public int? CategoryId { get; set; }   // null = Unclassified row
    public int Saudi { get; set; }
    public int NonSaudi { get; set; }
}

/// <summary>Body of POST /Saudization/SavePlan (new) and /Saudization/UpdatePlan (PlanId set).</summary>
public class SavePlanPayload
{
    public int SnapshotId { get; set; }
    public string? Label { get; set; }
    public int? PlanId { get; set; }   // set only for UpdatePlan — the scenario to overwrite
    public List<PostedCell> Cells { get; set; } = new();
}

/// <summary>Body of POST /Saudization/Export. Enhanced grid = the posted cells (exactly what is on screen);
/// original = recomputed from the snapshot. RecordId/CategoryId mirror the on-screen filter so the export matches.</summary>
public class ExportPayload
{
    public int SnapshotId { get; set; }
    public string Mode { get; set; } = "current";
    public int? PlanId { get; set; }        // used only to name the file after the loaded scenario
    public int? RecordId { get; set; }      // on-screen registration filter (null = all)
    public int? CategoryId { get; set; }    // on-screen category filter (null = all)
    public List<PostedCell> Cells { get; set; } = new();
}
