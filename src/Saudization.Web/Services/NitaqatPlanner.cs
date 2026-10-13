using Saudization.Web.Models;

namespace Saudization.Web.Services;

/// <summary>
/// Builds a per-registration "roadmap": for every Nitaqat level above a record's current level (up to
/// Platinum) it computes the three ways to get there — hire Saudi, swap non-Saudi→Saudi, remove non-Saudi —
/// plus a per-category breakdown that prioritises the categories below their own required % ("red first").
/// Read-only analysis on the live current data; reuses the per-cell actions already computed by
/// <see cref="NitaqatCalculator.Classify"/>.
/// </summary>
public static class NitaqatPlanner
{
    private sealed class Alloc { public CategoryRow Row = null!; public Cell Cell = null!; public int Red; public int Swap; public int Hire; }

    public static List<RecordRoadmap> Build(DashboardViewModel vm, bool isRtl)
    {
        string CatName(CategoryRow r) => isRtl ? r.NameAr : (string.IsNullOrWhiteSpace(r.NameEn) ? r.NameAr : r.NameEn!);
        var roadmaps = new List<RecordRoadmap>();

        foreach (var rc in vm.Records)
        {
            var rm = new RecordRoadmap
            {
                RecordId = rc.RecordId,
                Label = rc.Label,
                CurrentZone = rc.Zone,
                CurrentPct = rc.Pct,
                ZoneColor = rc.ZoneColor,
                Headcount = rc.Headcount,
                Saudi = rc.SaudiCountedGeneral,
                NonSaudi = rc.NonSaudi
            };
            roadmaps.Add(rm);
            if (rc.Headcount == 0) continue;

            int H = rc.Headcount, S = rc.SaudiCountedGeneral, N = rc.NonSaudi;

            // each Nitaqat threshold, in ascending order, that the record has NOT yet reached
            var targets = new (NitaqatZone zone, decimal t)[]
            {
                (NitaqatZone.LowGreen, rc.LowGreenMin),
                (NitaqatZone.MediumGreen, rc.MediumGreenMin),
                (NitaqatZone.HighGreen, rc.HighGreenMin),
                (NitaqatZone.Platinum, rc.PlatinumMin)
            };

            foreach (var (zone, T) in targets)
            {
                if (T <= rc.Pct || T <= 0m) continue;   // already reached (or an invalid threshold)

                var lp = new LevelPlan { Level = zone, TargetPct = T };

                // ---- three registration-level options ----
                lp.HireSaudi = T < 1m ? Math.Max(0, Ceil((T * H - S) / (1 - T))) : 0;

                int need = Math.Max(0, Ceil(T * H - S));      // net Saudi to add, headcount fixed
                lp.SwapNonSaudi = Math.Min(need, N);
                lp.SwapFeasible = need <= N;

                int keep = (int)Math.Floor(S / T);            // total workforce that still meets T with the same Saudi count
                int remove = H - keep;
                lp.RemoveNonSaudi = Math.Min(Math.Max(0, remove), N);
                lp.RemoveFeasible = S > 0 && remove >= 0 && remove <= N;

                // ---- per-category breakdown (red categories first) for the swap→hire path ----
                var allocs = new List<Alloc>();
                foreach (var row in vm.Rows)
                {
                    if (!row.ByRecord.TryGetValue(rc.RecordId, out var cell) || cell.Total == 0) continue;
                    allocs.Add(new Alloc { Row = row, Cell = cell, Red = cell.ReplaceNeeded ?? 0 });
                }

                int remaining = need;
                foreach (var a in allocs.OrderByDescending(x => x.Red))          // 1) fix reddest categories first
                {
                    if (remaining <= 0) break;
                    if (a.Red <= 0) continue;
                    int take = Math.Min(Math.Min(remaining, a.Red), a.Cell.NonSaudi);
                    a.Swap += take; remaining -= take;
                }
                if (remaining > 0)                                              // 2) spread across spare non-Saudi capacity
                    foreach (var a in allocs.OrderByDescending(x => x.Cell.NonSaudi - x.Swap))
                    {
                        if (remaining <= 0) break;
                        int avail = a.Cell.NonSaudi - a.Swap;
                        if (avail <= 0) continue;
                        int take = Math.Min(remaining, avail);
                        a.Swap += take; remaining -= take;
                    }
                if (remaining > 0)                                              // 3) not enough non-Saudi anywhere → hire the rest
                {
                    var t = allocs.OrderByDescending(x => x.Red).FirstOrDefault() ?? allocs.FirstOrDefault();
                    if (t != null) { t.Hire += remaining; remaining = 0; }
                }

                lp.Breakdown = allocs
                    .Where(a => a.Swap > 0 || a.Hire > 0 || a.Red > 0)
                    .OrderByDescending(a => a.Swap + a.Hire).ThenByDescending(a => a.Red)
                    .Select(a => new CategoryContribution
                    {
                        Category = CatName(a.Row),
                        Saudi = a.Cell.Saudi,
                        NonSaudi = a.Cell.NonSaudi,
                        Pct = a.Cell.Pct,
                        ReqPct = a.Row.RequiredPct,
                        Swap = a.Swap,
                        Hire = a.Hire,
                        IsRed = a.Cell.Status == CellStatus.Red
                    })
                    .ToList();

                rm.Levels.Add(lp);
            }
        }
        return roadmaps;
    }

    private static int Ceil(decimal v) => (int)Math.Ceiling(v);
}

// ---- read-only view-models for the Roadmap tab ----
public class RecordRoadmap
{
    public int RecordId { get; set; }
    public string Label { get; set; } = "";
    public NitaqatZone CurrentZone { get; set; }
    public decimal CurrentPct { get; set; }
    public string? ZoneColor { get; set; }
    public int Headcount { get; set; }
    public int Saudi { get; set; }
    public int NonSaudi { get; set; }
    public List<LevelPlan> Levels { get; set; } = new();
}

public class LevelPlan
{
    public NitaqatZone Level { get; set; }
    public decimal TargetPct { get; set; }
    public int HireSaudi { get; set; }
    public int SwapNonSaudi { get; set; }
    public bool SwapFeasible { get; set; }
    public int RemoveNonSaudi { get; set; }
    public bool RemoveFeasible { get; set; }
    public List<CategoryContribution> Breakdown { get; set; } = new();
}

public class CategoryContribution
{
    public string Category { get; set; } = "";
    public int Saudi { get; set; }
    public int NonSaudi { get; set; }
    public decimal Pct { get; set; }
    public decimal ReqPct { get; set; }
    public int Swap { get; set; }
    public int Hire { get; set; }
    public bool IsRed { get; set; }
}
