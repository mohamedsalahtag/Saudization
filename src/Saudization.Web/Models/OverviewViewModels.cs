namespace Saudization.Web.Models;

public enum FindingSeverity { High, Medium, Info }

public enum FindingKind
{
    RedRegistration,      // a registration overall in the red zone
    RedCategoryCell,      // a category below required in a registration
    UnmappedOccupations,  // occupations with no category → excluded from category %
    RegistrationNoRequired // registration has no manual required % set
}

public class Finding
{
    public FindingKind Kind { get; set; }
    public FindingSeverity Severity { get; set; }
    public string? Name { get; set; }    // registration or category
    public string? Name2 { get; set; }   // secondary (registration for a cell)
    public int Count { get; set; }
    public decimal Pct { get; set; }
    public int? RecordId { get; set; }
}

public class KpiTile
{
    public string LabelKey { get; set; } = "";
    public string Value { get; set; } = "";
    public string? Sub { get; set; }
    public string Tone { get; set; } = "";   // "" | ok | warn | bad
    public string? Href { get; set; }        // when set, the tile is a link (e.g. drill-down)
}

public class DashboardOverviewViewModel
{
    public bool HasData { get; set; }
    public int SnapshotId { get; set; }
    public Snapshot? Snapshot { get; set; }
    public List<Snapshot> Snapshots { get; set; } = new();

    public decimal OverallPct { get; set; }
    public NitaqatZone OverallZone { get; set; }
    public int Headcount, BaseSaudi, CountedSaudi, NonSaudi, SpecialCount, DisabledCount;
    public int RegCount, RedRegs, GreenRegs, PlatinumRegs, RedCellCount, UnmappedOccupations;

    public List<KpiTile> Kpis { get; set; } = new();
    public List<RecordColumn> Records { get; set; } = new();   // for the per-registration bar chart
    public int[] ZoneCounts { get; set; } = new int[6];        // indexed by (int)NitaqatZone
    public List<Finding> Findings { get; set; } = new();
    public List<TransferSuggestion> Suggestions { get; set; } = new();

    public TrendSeries? OverallTrend { get; set; }             // overall Saudization % across versions
    public List<Snapshot> TrendSnaps { get; set; } = new();
}
