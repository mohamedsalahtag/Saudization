using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Saudization.Web.Data;
using Saudization.Web.Services;

namespace Saudization.Web.Controllers;

public class VersionsController : Controller
{
    private readonly AppDbContext _db;
    private readonly NitaqatCalculator _calc;
    public VersionsController(AppDbContext db, NitaqatCalculator calc) { _db = db; _calc = calc; }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        // IgnoreQueryFilters so deleted versions (and their employee counts) can be listed for restore.
        var rows = await _db.Snapshots.IgnoreQueryFilters()
            .OrderByDescending(s => s.WeekDate).ThenByDescending(s => s.Id)
            .Select(s => new VersionListItem { Snapshot = s, Employees = s.Employees.Count })
            .ToListAsync(ct);
        return View(rows);
    }

    // Compare selected versions, or all versions between two dates (§3) — progress over time.
    public async Task<IActionResult> Compare(int[]? ids, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var all = await _db.Snapshots.OrderBy(s => s.WeekDate).ThenBy(s => s.Id).ToListAsync(ct);
        List<int> selected;
        if (from != null || to != null)
        {
            var lo = from?.Date ?? DateTime.MinValue;
            var hi = to?.Date ?? DateTime.MaxValue;
            selected = all.Where(s => s.WeekDate.Date >= lo && s.WeekDate.Date <= hi).Select(s => s.Id).ToList();
        }
        else
        {
            selected = (ids != null && ids.Length > 0)
                ? all.Where(s => ids.Contains(s.Id)).Select(s => s.Id).ToList()
                : all.Select(s => s.Id).ToList();
        }

        ViewBag.AllSnapshots = all;
        ViewBag.SelectedIds = selected;
        ViewBag.From = from?.ToString("yyyy-MM-dd");
        ViewBag.To = to?.ToString("yyyy-MM-dd");
        var vm = await _calc.BuildTrendAsync(selected, ct);
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        // Soft delete: the version moves to "Deleted versions" and can be restored.
        var s = await _db.Snapshots.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return RedirectToAction(nameof(Index));
        bool wasDefault = s.IsDefault;
        s.DeletedAtUtc = DateTime.UtcNow;
        s.DeletedByUser = User?.Identity?.Name ?? "web";
        s.IsDefault = false;
        await _db.SaveChangesAsync(ct);
        if (wasDefault)
        {
            // promote the most recently uploaded remaining version, not the latest week date
            var next = await _db.Snapshots.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
                .Select(x => (int?)x.Id).FirstOrDefaultAsync(ct);
            if (next is int n) await _db.SetDefaultSnapshotAsync(n, ct);
        }
        HttpContext.ClearSelectedSnapshot();
        TempData["ok"] = $"Version “{s.Label}” moved to Deleted versions — it can be restored from the bottom of this page.";
        return RedirectToAction(nameof(Index));
    }

    // Who joined, left or changed between two versions — matched by Iqama / ID, so a changed worker number is a change.
    public async Task<IActionResult> People(int? from, int? to, [FromServices] SnapshotComparer comparer, CancellationToken ct)
    {
        var snaps = await _db.Snapshots.OrderByDescending(s => s.WeekDate).ThenByDescending(s => s.Id).ToListAsync(ct);
        var vm = new Models.PeopleCompareViewModel { Snapshots = snaps };
        if (snaps.Count < 2) return View(vm);
        vm.ToId = to is int t && snaps.Any(s => s.Id == t) ? t : await _db.DefaultSnapshotIdAsync(ct);
        var toSnap = snaps.First(s => s.Id == vm.ToId);
        // default "from": the version just before "to" (by week, then upload order)
        vm.FromId = from is int f && snaps.Any(s => s.Id == f) ? f
            : snaps.Where(s => s.Id != vm.ToId && (s.WeekDate < toSnap.WeekDate || (s.WeekDate == toSnap.WeekDate && s.Id < toSnap.Id)))
                   .Select(s => s.Id).FirstOrDefault(snaps.First(s => s.Id != vm.ToId).Id);
        vm.Diff = await comparer.DiffAsync(vm.ToId, vm.FromId, ct);
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(int id, CancellationToken ct)
    {
        var s = await _db.Snapshots.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id && x.DeletedAtUtc != null, ct);
        if (s is not null)
        {
            s.DeletedAtUtc = null;
            s.DeletedByUser = null;
            await _db.SaveChangesAsync(ct);
            TempData["ok"] = $"Version “{s.Label}” restored. Use “Set default” if it should be shown by default.";
        }
        return RedirectToAction(nameof(Index));
    }

    // Permanent delete of a version that is already in "Deleted versions" (employees + scenarios cascade).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Purge(int id, CancellationToken ct)
    {
        var s = await _db.Snapshots.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id && x.DeletedAtUtc != null, ct);
        if (s is not null)
        {
            _db.Snapshots.Remove(s);
            await _db.SaveChangesAsync(ct);
            TempData["ok"] = $"Version “{s.Label}” permanently deleted.";
        }
        return RedirectToAction(nameof(Index));
    }

    // Make a version the default shown across the app.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetDefault(int id, CancellationToken ct)
    {
        if (await _db.Snapshots.AnyAsync(s => s.Id == id, ct))
        {
            await _db.SetDefaultSnapshotAsync(id, ct);
            HttpContext.ClearSelectedSnapshot();   // drop any remembered selection so the new default shows everywhere
            TempData["ok"] = "Default version updated.";
        }
        return RedirectToAction(nameof(Index));
    }
}

public class VersionListItem
{
    public Saudization.Web.Models.Snapshot Snapshot { get; set; } = null!;
    public int Employees { get; set; }
}
