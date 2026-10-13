using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Saudization.Web.Data;
using Saudization.Web.Models;

namespace Saudization.Web.Controllers;

/// <summary>Browse / add / delete the Saudization categories (فئات التوطين).
/// Deleting a category only unclassifies its occupations (FK is SetNull) — no occupation or employee row is lost.</summary>
public class CategoriesController : Controller
{
    private readonly AppDbContext _db;
    public CategoriesController(AppDbContext db) => _db = db;

    public async Task<IActionResult> Index(CancellationToken ct = default)
    {
        // occupations mapped per category (snapshot-independent) — this is what a delete would unclassify
        var occByCat = await _db.Occupations
            .Where(o => o.CategoryId != null)
            .GroupBy(o => o.CategoryId!.Value)
            .Select(g => new { CatId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CatId, x => x.Count, ct);

        // employees behind those occupations, latest version only (for a human-readable "reach")
        var sid = await _db.ResolveSnapshotAsync(HttpContext, null, ct);
        var empByCat = await _db.Employees
            .Where(e => e.SnapshotId == sid && e.Occupation != null && e.Occupation.CategoryId != null)
            .GroupBy(e => e.Occupation!.CategoryId!.Value)
            .Select(g => new { CatId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CatId, x => x.Count, ct);

        var rows = await _db.Categories
            .OrderBy(c => c.NameAr)
            .Select(c => new CatRow { Id = c.Id, NameAr = c.NameAr, NameEn = c.NameEn, CurrentRequiredPct = c.CurrentRequiredPct })
            .ToListAsync(ct);
        foreach (var r in rows)
        {
            r.OccCount = occByCat.GetValueOrDefault(r.Id, 0);
            r.EmpCount = empByCat.GetValueOrDefault(r.Id, 0);
        }

        ViewBag.OccMappedTotal = occByCat.Values.Sum();
        return View(rows);
    }

    // Add one category. NameAr is required and unique; NameEn and current % are optional.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string nameAr, string? nameEn, decimal? currentPct, CancellationToken ct)
    {
        var name = (nameAr ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) { TempData["err"] = "Enter a category name."; return RedirectToAction(nameof(Index)); }

        if (await _db.Categories.AnyAsync(c => c.NameAr == name, ct))
        { TempData["err"] = $"“{name}” already exists."; return RedirectToAction(nameof(Index)); }

        // accept the percentage either as 0..1 or as a whole number (30 → 0.30)
        decimal pct = currentPct is decimal p ? (p > 1m ? p / 100m : p) : 0m;
        if (pct < 0m) pct = 0m; else if (pct > 1m) pct = 1m;

        _db.Categories.Add(new SaudizationCategory
        {
            NameAr = name,
            NameEn = string.IsNullOrWhiteSpace(nameEn) ? null : nameEn!.Trim(),
            CurrentRequiredPct = pct,
            FutureRequiredPct = pct
        });
        await _db.SaveChangesAsync(ct);
        TempData["ok"] = $"Added “{name}”.";
        return RedirectToAction(nameof(Index));
    }

    // Delete one category. Its occupations are set to Unclassified (FK SetNull); they are not deleted.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var cat = await _db.Categories.FindAsync(new object[] { id }, ct);
        if (cat is null) { TempData["err"] = "Category not found."; return RedirectToAction(nameof(Index)); }
        var occ = await _db.Occupations.CountAsync(o => o.CategoryId == id, ct);
        var name = cat.NameAr;
        _db.Categories.Remove(cat);
        await _db.SaveChangesAsync(ct);
        TempData["ok"] = occ > 0
            ? $"Deleted “{name}”. {occ} occupation(s) are now unclassified."
            : $"Deleted “{name}”.";
        return RedirectToAction(nameof(Index));
    }

    // Delete every category. All mapped occupations become Unclassified until re-mapped or re-uploaded.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAll(CancellationToken ct)
    {
        var cats = await _db.Categories.ToListAsync(ct);
        if (cats.Count == 0) { TempData["err"] = "There are no categories to delete."; return RedirectToAction(nameof(Index)); }
        var affected = await _db.Occupations.CountAsync(o => o.CategoryId != null, ct);
        _db.Categories.RemoveRange(cats);
        await _db.SaveChangesAsync(ct);
        TempData["ok"] = $"Deleted all {cats.Count} categories. {affected} occupation(s) are now unclassified — upload a workbook (with the دليل المهن sheet) to re-map them.";
        return RedirectToAction(nameof(Index));
    }
}

public class CatRow
{
    public int Id { get; set; }
    public string NameAr { get; set; } = "";
    public string? NameEn { get; set; }
    public decimal CurrentRequiredPct { get; set; }
    public int OccCount { get; set; }
    public int EmpCount { get; set; }
}
