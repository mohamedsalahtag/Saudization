using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Saudization.Web.Data;
using Saudization.Web.Models;

namespace Saudization.Web.Services;

/// <summary>Assembles the Dashboard overview: KPIs, findings/issues, suggestions and chart data.</summary>
public class OverviewBuilder
{
    private readonly AppDbContext _db;
    private readonly NitaqatCalculator _calc;
    private readonly TransferAdvisor _advisor;
    public OverviewBuilder(AppDbContext db, NitaqatCalculator calc, TransferAdvisor advisor)
    { _db = db; _calc = calc; _advisor = advisor; }

    /// <param name="full">false = KPIs + registration columns only (no suggestions, no trend across versions).</param>
    public async Task<DashboardOverviewViewModel> BuildAsync(int? snapshotId, CancellationToken ct = default, bool full = true)
    {
        var snaps = await _db.Snapshots.OrderByDescending(s => s.WeekDate).ThenByDescending(s => s.Id).ToListAsync(ct);
        var o = new DashboardOverviewViewModel { Snapshots = snaps };
        if (snaps.Count == 0) return o;

        o.HasData = true;
        // resolve the requested version, falling back to the default (else latest) if it's missing (e.g. a stale bookmark)
        o.Snapshot = (snapshotId is int id ? snaps.FirstOrDefault(s => s.Id == id) : null)
            ?? snaps.FirstOrDefault(s => s.IsDefault) ?? snaps.First();
        o.SnapshotId = o.Snapshot.Id;

        var vm = await _calc.BuildAsync(DashboardMode.Current, null, o.SnapshotId, ct);
        o.Records = vm.Records;
        o.OverallPct = vm.GrandPct;
        o.OverallZone = vm.GrandZone;
        o.Headcount = vm.GrandHeadcount;
        o.BaseSaudi = vm.GrandBaseSaudi;
        o.CountedSaudi = vm.GrandSaudiCounted;
        o.NonSaudi = vm.GrandHeadcount - vm.GrandBaseSaudi;
        o.RegCount = vm.Records.Count;

        foreach (var rc in vm.Records)
        {
            o.ZoneCounts[(int)rc.Zone]++;
            if (rc.Zone == NitaqatZone.Red) o.RedRegs++;
            else if (rc.Zone == NitaqatZone.Platinum) o.PlatinumRegs++;
            else if (rc.Zone is NitaqatZone.LowGreen or NitaqatZone.MediumGreen or NitaqatZone.HighGreen) o.GreenRegs++;
        }

        // red category cells → findings
        var redCells = new List<Finding>();
        foreach (var row in vm.Rows)
        {
            if (row.IsUnclassified) continue;
            foreach (var rc in vm.Records)
            {
                if (!row.ByRecord.TryGetValue(rc.RecordId, out var cell) || cell.Status != CellStatus.Red) continue;
                o.RedCellCount++;
                redCells.Add(new Finding
                {
                    Kind = FindingKind.RedCategoryCell,
                    Severity = FindingSeverity.Medium,
                    Name = row.NameAr,
                    Name2 = rc.Label,
                    Count = cell.ReplaceNeeded ?? 0,
                    Pct = cell.Pct,
                    RecordId = rc.RecordId
                });
            }
        }

        // special / disabled tallies for this version
        o.SpecialCount = await _db.Employees.CountAsync(e => e.SnapshotId == o.SnapshotId && e.CountsAsSaudiSpecial, ct);
        o.DisabledCount = await _db.Employees.CountAsync(e => e.SnapshotId == o.SnapshotId && e.DisabilityCountAs > 1, ct);

        // unmapped occupations (appear on employees, no category) for this version
        o.UnmappedOccupations = await _db.Employees
            .Where(e => e.SnapshotId == o.SnapshotId && e.OccupationRaw != null && (e.Occupation == null || e.Occupation.CategoryId == null))
            .Select(e => e.OccupationRaw!).Distinct().CountAsync(ct);

        // ---- KPI tiles ----
        string P(decimal p) => (p * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";
        o.Kpis.Add(new KpiTile { LabelKey = "Kpi_Overall", Value = P(o.OverallPct), Sub = $"{o.CountedSaudi} / {o.Headcount}",
            Tone = o.OverallZone == NitaqatZone.Red ? "bad" : o.OverallZone == NitaqatZone.Platinum ? "ok" : "" });
        o.Kpis.Add(new KpiTile { LabelKey = "Kpi_Headcount", Value = o.Headcount.ToString(), Sub = $"{o.NonSaudi} + {o.BaseSaudi}" });
        o.Kpis.Add(new KpiTile { LabelKey = "Kpi_RedRegs", Value = o.RedRegs.ToString(), Sub = $"/ {o.RegCount}", Tone = o.RedRegs > 0 ? "bad" : "ok" });
        o.Kpis.Add(new KpiTile { LabelKey = "Kpi_RedCells", Value = o.RedCellCount.ToString(), Tone = o.RedCellCount > 0 ? "warn" : "ok" });
        var weightHref = $"/Dashboard/Weighting?snapshotId={o.SnapshotId}";
        o.Kpis.Add(new KpiTile { LabelKey = "Kpi_Special", Value = o.SpecialCount.ToString(), Href = weightHref });
        o.Kpis.Add(new KpiTile { LabelKey = "Kpi_Disabled", Value = o.DisabledCount.ToString(), Href = weightHref });

        // ---- findings (most severe first) ----
        foreach (var rc in vm.Records.Where(r => r.Zone == NitaqatZone.Red))
            o.Findings.Add(new Finding { Kind = FindingKind.RedRegistration, Severity = FindingSeverity.High, Name = rc.Label, Pct = rc.Pct, RecordId = rc.RecordId });
        o.Findings.AddRange(redCells.OrderByDescending(f => f.Count).Take(8));
        if (o.UnmappedOccupations > 0)
            o.Findings.Add(new Finding { Kind = FindingKind.UnmappedOccupations, Severity = FindingSeverity.Info, Count = o.UnmappedOccupations });
        foreach (var rc in vm.Records.Where(r => r.RequiredPct <= 0))
            o.Findings.Add(new Finding { Kind = FindingKind.RegistrationNoRequired, Severity = FindingSeverity.Info, Name = rc.Label, RecordId = rc.RecordId });

        // ---- suggestions ----
        if (!full) return o;
        o.Suggestions = _advisor.Suggest(vm).Take(6).ToList();   // same calculation, not rebuilt

        // ---- trend across all versions ----
        var trend = await _calc.BuildTrendAsync(snaps.Select(s => s.Id).ToList(), ct);
        o.OverallTrend = trend.Overall;
        o.TrendSnaps = trend.Snapshots;

        return o;
    }
}
