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
        var rows = await _db.Snapshots
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
        var s = await _db.Snapshots.FindAsync([id], ct);
        if (s is not null) { _db.Snapshots.Remove(s); await _db.SaveChangesAsync(ct); TempData["ok"] = "Version deleted."; }
        return RedirectToAction(nameof(Index));
    }
}

public class VersionListItem
{
    public Saudization.Web.Models.Snapshot Snapshot { get; set; } = null!;
    public int Employees { get; set; }
}
