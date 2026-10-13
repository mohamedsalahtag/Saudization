namespace Saudization.Web.Models;

public enum DashboardMode { Current, Plan }

/// <summary>Per-occupation-cell compliance colour vs the required % (§7). Exempt = below MinForCalc.</summary>
public enum CellStatus { NA, Red, LightGreen, HighGreen, Exempt }

/// <summary>One Saudi/non-Saudi tally for a (category × record) intersection or a total.</summary>
public class Cell
{
    public int Saudi { get; set; }
    public int NonSaudi { get; set; }
    public int Total => Saudi + NonSaudi;
    public decimal Pct => Total > 0 ? (decimal)Saudi / Total : 0m;

    /// <summary>Required-Saudi shortfall: &gt;0 = Saudis still to hire, &lt;0 = surplus. Null when the category is exempt.</summary>
    public int? Gap { get; set; }

    public CellStatus Status { get; set; } = CellStatus.NA;

    /// <summary>§7 red-cell actions to reach the required %. Set only when Status == Red.</summary>
    public int? ReplaceNeeded { get; set; }   // إحلال: swap non-Saudi → Saudi (total unchanged)
    public int? HireNeeded { get; set; }       // توظيف: add Saudi (total grows)
    public int? ShedNonSaudi { get; set; }     // red: remove this many non-Saudi to reach req by removal (T − ⌊S/req⌋)
    public int? ShedToExemptNonSaudi { get; set; } // red: remove this many non-Saudi to drop below MinForCalc → exempt (T − (min−1))

    /// <summary>Green-cell headroom. Set only when Status is Light/High green.</summary>
    public int? AbsorbNonSaudi { get; set; }   // non-Saudi it can still add staying ≥ req (⌊S/req⌋ − T)
    public int? SaudiSurplus { get; set; }     // Saudis it can release staying ≥ req (S − ceil(req·T))

    /// <summary>Exempt-cell (below MinForCalc) headroom. Set only when Status == Exempt.</summary>
    public int? ExemptAddNonSaudi { get; set; } // add this many non-Saudi and stay uncounted (min−1−T)
    public int? ExemptSaudiDrop { get; set; }   // Saudis safe to drop while staying exempt (= all of them)
}

/// <summary>A registration column header with its aggregated general tally and Nitaqat zone (§7).</summary>
public class RecordColumn
{
    public int RecordId { get; set; }
    public string Label { get; set; } = "";
    public string? NameAr { get; set; }
    public string? RegistrationNumber { get; set; }

    public int Headcount { get; set; }                // actual employees (denominator)
    public int BaseSaudi { get; set; }                // Saudi nationals + special categories
    public int NonSaudi => Headcount - BaseSaudi;
    public int DisabledExtra { get; set; }            // capped disability bonus added to the general Saudi count
    public int DisabledExtraRaw { get; set; }         // uncapped disability bonus (for live client rollup)
    public int SaudiCountedGeneral => BaseSaudi + DisabledExtra;
    public decimal Pct => Headcount > 0 ? (decimal)SaudiCountedGeneral / Headcount : 0m;

    public NitaqatZone Zone { get; set; }
    public decimal RequiredPct { get; set; }          // manual registration target
    public decimal DisabledPct { get; set; }          // نسبة ذوي الاحتياجات الخاصة (share of workforce)
    public string? ZoneColor { get; set; }            // resolved hex (override or default)
    public decimal LowGreenMin { get; set; }          // Red→Green boundary (leave-red threshold)
    public decimal MediumGreenMin { get; set; }       // Nitaqat zone thresholds (for the Roadmap tab)
    public decimal HighGreenMin { get; set; }
    public decimal PlatinumMin { get; set; }

    /// <summary>Saudis to hire to lift the general % up to LowGreenMin (leave Red). 0 when already green.</summary>
    public int HiresToGreen =>
        Zone != NitaqatZone.Red || Headcount == 0 || LowGreenMin >= 1m ? 0
        : Math.Max(0, (int)Math.Ceiling((LowGreenMin * Headcount - SaudiCountedGeneral) / (1 - LowGreenMin)));
}

public class CategoryRow
{
    public int? CategoryId { get; set; }
    public string NameAr { get; set; } = "";
    public string? NameEn { get; set; }
    public int MinForCalc { get; set; }
    public decimal RequiredPct { get; set; }
    public Dictionary<int, Cell> ByRecord { get; set; } = new();   // key = RecordId
    public Cell Grand { get; set; } = new();
    public bool IsUnclassified { get; set; }
}

public class DashboardViewModel
{
    public DashboardMode Mode { get; set; }
    public List<RecordColumn> Records { get; set; } = new();
    public List<CategoryRow> Rows { get; set; } = new();

    // grand totals across the (filtered) scope
    public Cell Grand { get; set; } = new();          // occupation-based (national + special) for the category-grand column
    public int GrandHeadcount { get; set; }
    public int GrandBaseSaudi { get; set; }
    public int GrandDisabledExtra { get; set; }
    public int GrandSaudiCounted => GrandBaseSaudi + GrandDisabledExtra;
    public decimal GrandPct => GrandHeadcount > 0 ? (decimal)GrandSaudiCounted / GrandHeadcount : 0m;
    public NitaqatZone GrandZone { get; set; }

    // optional filters echoed back to the view
    public int? FilterRecordId { get; set; }
    public int? FilterCategoryId { get; set; }

    // the version being shown
    public int SnapshotId { get; set; }
    public Snapshot? Snapshot { get; set; }
}

// ---- version comparison (progress over time) ----

public class TrendPoint
{
    public int SnapshotId { get; set; }
    public int Saudi { get; set; }
    public int Total { get; set; }
    public decimal Pct => Total > 0 ? (decimal)Saudi / Total : 0m;
    public NitaqatZone Zone { get; set; }
}

public class TrendSeries
{
    public int? RecordId { get; set; }         // null = overall
    public string Label { get; set; } = "";
    public List<TrendPoint> Points { get; set; } = new();   // aligned to ComparisonViewModel.Snapshots order
}

public class ComparisonViewModel
{
    public List<Snapshot> Snapshots { get; set; } = new();
    public TrendSeries Overall { get; set; } = new();
    public List<TrendSeries> Series { get; set; } = new();  // per registration
}
