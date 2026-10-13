using Saudization.Web.Services;

namespace Saudization.Web.Models;

/// <summary>The upload preview: what the file contains and what saving it would change. Nothing is saved yet.</summary>
public class ImportPreviewViewModel
{
    public string Token { get; set; } = "";
    public PendingUploadMeta Meta { get; set; } = new();
    public ImportResult Result { get; set; } = new();
    public int Employees { get; set; }
    public int Saudi { get; set; }
    /// <summary>An existing version with exactly the same employees — the upload can't be saved.</summary>
    public Snapshot? Identical { get; set; }
    /// <summary>Compared with the current default version (null when there is none yet).</summary>
    public SnapshotDiff? Diff { get; set; }
    public List<string> SameWeek { get; set; } = new();
    public bool SuggestDefault { get; set; } = true;
}

/// <summary>"Who joined, left or changed" between two versions.</summary>
public class PeopleCompareViewModel
{
    public List<Snapshot> Snapshots { get; set; } = new();
    public int FromId { get; set; }
    public int ToId { get; set; }
    public SnapshotDiff? Diff { get; set; }
}
