namespace Saudization.Web.Models;

/// <summary>A single suggested employee move between registrations to improve Saudization (§8).</summary>
public class TransferSuggestion
{
    public string CategoryName { get; set; } = "";
    public bool BringSaudi { get; set; }          // true = move a Saudi in; false = move a non-Saudi out
    public string FromLabel { get; set; } = "";
    public string ToLabel { get; set; } = "";

    public decimal FromPctBefore { get; set; }
    public decimal FromPctAfter { get; set; }
    public decimal ToPctBefore { get; set; }
    public decimal ToPctAfter { get; set; }

    public string Rationale { get; set; } = "";
}

public class TransfersViewModel
{
    public int SnapshotId { get; set; }
    public Snapshot? Snapshot { get; set; }
    public List<TransferSuggestion> Suggestions { get; set; } = new();
}

// ---- simulator (§9) — serialized to the client, recomputed live, never persisted ----

public class SimRecord
{
    public int Id { get; set; }
    public string Label { get; set; } = "";
    public int Saudi { get; set; }        // counted Saudi (general, incl. special + capped disability)
    public int NonSaudi { get; set; }
    public decimal Low { get; set; }
    public decimal Medium { get; set; }
    public decimal High { get; set; }
    public decimal Platinum { get; set; }
}

public class SimJobCell
{
    public int RecordId { get; set; }
    public int Saudi { get; set; }        // occupation Saudi (national + special)
    public int NonSaudi { get; set; }
}

public class SimCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Required { get; set; }
    public int MinForCalc { get; set; }
    public List<SimJobCell> Cells { get; set; } = new();   // aligned to SimulatorViewModel.Records order
}

public class SimulatorViewModel
{
    public int SnapshotId { get; set; }
    public Snapshot? Snapshot { get; set; }
    public List<SimRecord> Records { get; set; } = new();
    public List<SimCategory> Categories { get; set; } = new();
    public decimal LightGreenBand { get; set; } = 0.05m;
}
