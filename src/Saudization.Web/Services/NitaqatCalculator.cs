using Microsoft.EntityFrameworkCore;
using Saudization.Web.Data;
using Saudization.Web.Models;

namespace Saudization.Web.Services;

/// <summary>
/// Rebuilds the Nationalization dashboard live from the database. Two distinct Saudi tallies (spec §4/§5):
///  • occupation/category Saudi = Saudi nationals + special categories (no disability weighting);
///  • general/registration Saudi = the above + a capped disability "يُحتسب بعدد" bonus.
/// </summary>
public class NitaqatCalculator
{
    private readonly AppDbContext _db;
    public NitaqatCalculator(AppDbContext db) => _db = db;

    public static NitaqatZone ZoneFor(decimal pct, Record r) =>
        pct >= r.PlatinumMin ? NitaqatZone.Platinum
        : pct >= r.HighGreenMin ? NitaqatZone.HighGreen
        : pct >= r.MediumGreenMin ? NitaqatZone.MediumGreen
        : pct >= r.LowGreenMin ? NitaqatZone.LowGreen
        : NitaqatZone.Red;

    public async Task<Settings> GetSettingsAsync(CancellationToken ct = default) =>
        await _db.Settings.FirstOrDefaultAsync(ct) ?? new Settings();

    public async Task<DashboardViewModel> BuildAsync(DashboardMode mode, int? filterRecordId, int snapshotId, CancellationToken ct = default)
    {
        var records = await _db.Records
            .Where(r => filterRecordId == null || r.Id == filterRecordId)
            .OrderBy(r => r.RegistrationLabel)
            .ToListAsync(ct);
        var recById = records.ToDictionary(r => r.Id);
        var recIds = records.Select(r => r.Id).ToHashSet();
        var categories = await _db.Categories.OrderBy(c => c.NameAr).ToListAsync(ct);
        var settings = await GetSettingsAsync(ct);

        // per-employee facts for the selected version
        var emps = await _db.Employees
            .Where(e => e.SnapshotId == snapshotId && recIds.Contains(e.Facility!.RecordId))
            .Select(e => new
            {
                RecordId = e.Facility!.RecordId,
                IsSaudiNational = e.Nationality != null && e.Nationality.IsSaudi,
                Special = e.CountsAsSaudiSpecial,
                CountAs = e.DisabilityCountAs,
                CategoryId = e.Occupation != null ? e.Occupation.CategoryId : (int?)null
            })
            .ToListAsync(ct);

        var vm = new DashboardViewModel
        {
            Mode = mode,
            FilterRecordId = filterRecordId,
            SnapshotId = snapshotId,
            Snapshot = await _db.Snapshots.FindAsync(new object[] { snapshotId }, ct)
        };

        // ---- category × registration cells (occupation Saudi = national OR special) ----
        var rows = new Dictionary<int, CategoryRow>();
        foreach (var c in categories)
            rows[c.Id] = new CategoryRow
            {
                CategoryId = c.Id,
                NameAr = c.NameAr,
                NameEn = c.NameEn,
                MinForCalc = c.MinForCalc,
                RequiredPct = mode == DashboardMode.Plan ? c.FutureRequiredPct : c.CurrentRequiredPct
            };
        CategoryRow? unclassified = null;
        CategoryRow Unclassified() => unclassified ??=
            new CategoryRow { CategoryId = null, NameAr = "غير مصنّف", NameEn = "Unclassified", IsUnclassified = true };

        // ---- registration aggregates ----
        var agg = records.ToDictionary(r => r.Id, r => new RecAgg());

        foreach (var e in emps)
        {
            bool occSaudi = e.IsSaudiNational || e.Special;   // occupation-level Saudi
            var row = (e.CategoryId is int cid && rows.TryGetValue(cid, out var known)) ? known : Unclassified();
            var cell = row.ByRecord.TryGetValue(e.RecordId, out var c) ? c : (row.ByRecord[e.RecordId] = new Cell());
            Add(cell, occSaudi);
            Add(row.Grand, occSaudi);

            var a = agg[e.RecordId];
            a.Headcount++;
            if (occSaudi) a.BaseSaudi++;
            if (e.CountAs > 1) { a.DisabledExtraRaw += e.CountAs - 1; a.DisabledCount++; }
        }

        // record columns with capped disability bonus + zone
        foreach (var r in records)
        {
            var a = agg[r.Id];
            int cap = (int)Math.Floor(settings.DisabilityCapPct * a.BaseSaudi);
            int extra = Math.Min(a.DisabledExtraRaw, cap);
            decimal pct = a.Headcount > 0 ? (decimal)(a.BaseSaudi + extra) / a.Headcount : 0m;
            var zone = a.Headcount > 0 ? ZoneFor(pct, r) : NitaqatZone.Unknown;

            vm.Records.Add(new RecordColumn
            {
                RecordId = r.Id,
                Label = r.RegistrationLabel,
                NameAr = r.NameAr,
                RegistrationNumber = r.RegistrationNumber,
                Headcount = a.Headcount,
                BaseSaudi = a.BaseSaudi,
                DisabledExtra = extra,
                DisabledExtraRaw = a.DisabledExtraRaw,
                Zone = zone,
                RequiredPct = r.RequiredPct,
                DisabledPct = a.Headcount > 0 ? (decimal)a.DisabledCount / a.Headcount : 0m,
                ZoneColor = ResolveZoneColor(r, zone),
                LowGreenMin = r.LowGreenMin
            });

            vm.GrandHeadcount += a.Headcount;
            vm.GrandBaseSaudi += a.BaseSaudi;
            vm.GrandDisabledExtra += extra;
            Add(vm.Grand, a.BaseSaudi, a.Headcount - a.BaseSaudi);
        }

        // ---- per-cell status + red-cell actions (§7) ----
        var allRows = rows.Values.ToList();
        if (unclassified != null) allRows.Add(unclassified);
        foreach (var row in allRows)
        {
            foreach (var (_, cell) in row.ByRecord) Classify(row, cell, settings.LightGreenBand);
            Classify(row, row.Grand, settings.LightGreenBand);
        }
        vm.Rows = allRows.OrderBy(r => r.IsUnclassified ? 1 : 0).ThenBy(r => r.NameAr).ToList();

        var refRecord = records.FirstOrDefault();
        vm.GrandZone = (refRecord != null && vm.GrandHeadcount > 0) ? ZoneFor(vm.GrandPct, refRecord) : NitaqatZone.Unknown;
        return vm;
    }

    /// <summary>Builds a dashboard VM from posted aggregate counts (edited grid / saved plan) instead of live
    /// employees. Required %, MinForCalc, zone thresholds and disability weighting come from the DB (authoritative);
    /// only the Saudi/non-Saudi counts come from the caller.</summary>
    public async Task<DashboardViewModel> BuildFromCountsAsync(DashboardMode mode, int snapshotId, IReadOnlyList<PostedCell> cells, CancellationToken ct = default)
    {
        var records = await _db.Records.OrderBy(r => r.RegistrationLabel).ToListAsync(ct);
        var recIds = records.Select(r => r.Id).ToHashSet();
        var categories = await _db.Categories.OrderBy(c => c.NameAr).ToListAsync(ct);
        var settings = await GetSettingsAsync(ct);

        // disability weighting is a property of the real workforce; edits to counts don't change it.
        var disRaw = await _db.Employees
            .Where(e => e.SnapshotId == snapshotId && e.DisabilityCountAs > 1 && recIds.Contains(e.Facility!.RecordId))
            .Select(e => new { RecordId = e.Facility!.RecordId, Extra = e.DisabilityCountAs - 1 })
            .ToListAsync(ct);
        var disByRec = disRaw.GroupBy(x => x.RecordId).ToDictionary(g => g.Key, g => g.Sum(x => x.Extra));

        var vm = new DashboardViewModel
        {
            Mode = mode,
            SnapshotId = snapshotId,
            Snapshot = await _db.Snapshots.FindAsync(new object[] { snapshotId }, ct)
        };

        var rows = new Dictionary<int, CategoryRow>();
        foreach (var c in categories)
            rows[c.Id] = new CategoryRow
            {
                CategoryId = c.Id, NameAr = c.NameAr, NameEn = c.NameEn, MinForCalc = c.MinForCalc,
                RequiredPct = mode == DashboardMode.Plan ? c.FutureRequiredPct : c.CurrentRequiredPct
            };
        CategoryRow? unclassified = null;
        CategoryRow Unclassified() => unclassified ??=
            new CategoryRow { CategoryId = null, NameAr = "غير مصنّف", NameEn = "Unclassified", IsUnclassified = true };

        var agg = records.ToDictionary(r => r.Id, r => new RecAgg());
        foreach (var pc in cells)
        {
            if (!recIds.Contains(pc.RecordId)) continue;
            int s = Math.Max(0, pc.Saudi), n = Math.Max(0, pc.NonSaudi);
            var row = (pc.CategoryId is int cid && rows.TryGetValue(cid, out var known)) ? known : Unclassified();
            var cell = row.ByRecord.TryGetValue(pc.RecordId, out var ex) ? ex : (row.ByRecord[pc.RecordId] = new Cell());
            cell.Saudi += s; cell.NonSaudi += n;
            row.Grand.Saudi += s; row.Grand.NonSaudi += n;
            var a = agg[pc.RecordId];
            a.Headcount += s + n; a.BaseSaudi += s;
        }

        foreach (var r in records)
        {
            var a = agg[r.Id];
            int raw = disByRec.TryGetValue(r.Id, out var dv) ? dv : 0;
            int cap = (int)Math.Floor(settings.DisabilityCapPct * a.BaseSaudi);
            int extra = Math.Min(raw, cap);
            decimal pct = a.Headcount > 0 ? (decimal)(a.BaseSaudi + extra) / a.Headcount : 0m;
            var zone = a.Headcount > 0 ? ZoneFor(pct, r) : NitaqatZone.Unknown;
            vm.Records.Add(new RecordColumn
            {
                RecordId = r.Id, Label = r.RegistrationLabel, NameAr = r.NameAr, RegistrationNumber = r.RegistrationNumber,
                Headcount = a.Headcount, BaseSaudi = a.BaseSaudi, DisabledExtra = extra, DisabledExtraRaw = raw,
                Zone = zone, RequiredPct = r.RequiredPct, ZoneColor = ResolveZoneColor(r, zone), LowGreenMin = r.LowGreenMin
            });
            vm.GrandHeadcount += a.Headcount; vm.GrandBaseSaudi += a.BaseSaudi; vm.GrandDisabledExtra += extra;
            Add(vm.Grand, a.BaseSaudi, a.Headcount - a.BaseSaudi);
        }

        var allRows = rows.Values.ToList();
        if (unclassified != null) allRows.Add(unclassified);
        foreach (var row in allRows)
        {
            foreach (var (_, cell) in row.ByRecord) Classify(row, cell, settings.LightGreenBand);
            Classify(row, row.Grand, settings.LightGreenBand);
        }
        vm.Rows = allRows.OrderBy(r => r.IsUnclassified ? 1 : 0).ThenBy(r => r.NameAr).ToList();

        var refRecord = records.FirstOrDefault();
        vm.GrandZone = (refRecord != null && vm.GrandHeadcount > 0) ? ZoneFor(vm.GrandPct, refRecord) : NitaqatZone.Unknown;
        return vm;
    }

    /// <summary>Assigns a cell's compliance status + the full action set (§7, extended). Public so the
    /// export/plan paths can reuse the exact same formulas the dashboard uses.</summary>
    public static void Classify(CategoryRow row, Cell cell, decimal band)
    {
        cell.Status = CellStatus.NA;
        cell.Gap = null; cell.ReplaceNeeded = null; cell.HireNeeded = null; cell.ShedNonSaudi = null;
        cell.AbsorbNonSaudi = null; cell.SaudiSurplus = null; cell.ExemptAddNonSaudi = null; cell.ExemptSaudiDrop = null;
        if (row.IsUnclassified || row.RequiredPct <= 0) return;

        decimal req = row.RequiredPct;
        int T = cell.Total, S = cell.Saudi;

        if (T == 0) return;                                      // no employees here → NA (renders 0%, no action)

        if (T < row.MinForCalc)                                  // below the calculation floor → exempt/safe
        {
            cell.Status = CellStatus.Exempt;
            cell.ExemptAddNonSaudi = Math.Max(0, row.MinForCalc - 1 - T);   // add this many non-Saudi & stay uncounted
            cell.ExemptSaudiDrop = S;                                       // all Saudis removable, stays exempt
            return;
        }

        int need = (int)Math.Ceiling(req * T);
        cell.Gap = need - S;                                     // + shortfall, − surplus
        int cap = req > 0 ? (int)Math.Floor(S / req) : T;        // max total T that keeps p ≥ req for this S

        if (cell.Pct < req)
        {
            cell.Status = CellStatus.Red;
            cell.ReplaceNeeded = Math.Max(0, need - S);                     // swap non-Saudi → Saudi (total unchanged)
            cell.HireNeeded = req < 1m                                      // add Saudi (total grows)
                ? (int)Math.Ceiling((req * T - S) / (1 - req))
                : Math.Max(0, T - S);
            cell.ShedNonSaudi = Math.Max(0, T - cap);                       // remove non-Saudi to reach req by removal
        }
        else
        {
            cell.Status = cell.Pct <= req + band ? CellStatus.LightGreen : CellStatus.HighGreen;
            cell.AbsorbNonSaudi = Math.Max(0, cap - T);                     // non-Saudi it can still add staying ≥ req
            cell.SaudiSurplus = Math.Max(0, S - need);                      // Saudis it can release staying ≥ req
        }
    }

    private static string ResolveZoneColor(Record r, NitaqatZone z)
    {
        var custom = z switch
        {
            NitaqatZone.Red => r.RedColor,
            NitaqatZone.LowGreen => r.LowGreenColor,
            NitaqatZone.MediumGreen => r.MediumGreenColor,
            NitaqatZone.HighGreen => r.HighGreenColor,
            NitaqatZone.Platinum => r.PlatinumColor,
            _ => null
        };
        return string.IsNullOrWhiteSpace(custom) ? ZoneDisplay.DefaultColor(z) : custom!;
    }

    private sealed class RecAgg { public int Headcount, BaseSaudi, DisabledExtraRaw, DisabledCount; }

    /// <summary>Overall + per-registration weighted Saudi% for each selected version, ordered by week.</summary>
    public async Task<ComparisonViewModel> BuildTrendAsync(IReadOnlyList<int> snapshotIds, CancellationToken ct = default)
    {
        var snaps = await _db.Snapshots
            .Where(s => snapshotIds.Contains(s.Id))
            .OrderBy(s => s.WeekDate).ThenBy(s => s.Id)
            .ToListAsync(ct);
        var records = await _db.Records.OrderBy(r => r.RegistrationLabel).ToListAsync(ct);
        var settings = await GetSettingsAsync(ct);

        var raw = await _db.Employees
            .Where(e => snapshotIds.Contains(e.SnapshotId))
            .Select(e => new
            {
                e.SnapshotId,
                RecordId = e.Facility!.RecordId,
                Base = (e.Nationality != null && e.Nationality.IsSaudi) || e.CountsAsSaudiSpecial,
                e.DisabilityCountAs
            })
            .ToListAsync(ct);

        // aggregate to (snapshot, record): headcount, baseSaudi, disability extra
        var agg = new Dictionary<(int, int), (int head, int baseS, int extra)>();
        foreach (var e in raw)
        {
            agg.TryGetValue((e.SnapshotId, e.RecordId), out var v);
            v.head++;
            if (e.Base) v.baseS++;
            if (e.DisabilityCountAs > 1) v.extra += e.DisabilityCountAs - 1;
            agg[(e.SnapshotId, e.RecordId)] = v;
        }
        (int saudi, int total) Weighted(int sid, int rid)
        {
            if (!agg.TryGetValue((sid, rid), out var v) || v.head == 0) return (0, 0);
            int cap = (int)Math.Floor(settings.DisabilityCapPct * v.baseS);
            return (v.baseS + Math.Min(v.extra, cap), v.head);
        }

        var vm = new ComparisonViewModel { Snapshots = snaps };
        foreach (var r in records)
        {
            var series = new TrendSeries { RecordId = r.Id, Label = r.RegistrationLabel };
            foreach (var s in snaps)
            {
                var (saudi, total) = Weighted(s.Id, r.Id);
                var pt = new TrendPoint { SnapshotId = s.Id, Saudi = saudi, Total = total };
                pt.Zone = total > 0 ? ZoneFor(pt.Pct, r) : NitaqatZone.Unknown;
                series.Points.Add(pt);
            }
            vm.Series.Add(series);
        }
        vm.Overall = new TrendSeries { RecordId = null, Label = "Overall" };
        foreach (var s in snaps)
        {
            int saudi = 0, total = 0;
            foreach (var r in records) { var (sd, tt) = Weighted(s.Id, r.Id); saudi += sd; total += tt; }
            vm.Overall.Points.Add(new TrendPoint { SnapshotId = s.Id, Saudi = saudi, Total = total });
        }
        return vm;
    }

    private static void Add(Cell c, bool isSaudi) { if (isSaudi) c.Saudi++; else c.NonSaudi++; }
    private static void Add(Cell c, int saudi, int nonSaudi) { c.Saudi += saudi; c.NonSaudi += nonSaudi; }
}
