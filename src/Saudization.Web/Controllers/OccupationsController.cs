using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Saudization.Web.Data;
using Saudization.Web.Models;
using Saudization.Web.Services;

namespace Saudization.Web.Controllers;

public class OccupationsController : Controller
{
    private readonly AppDbContext _db;
    private readonly ExcelImporter _importer;
    public OccupationsController(AppDbContext db, ExcelImporter importer) { _db = db; _importer = importer; }

    public async Task<IActionResult> Index(int? snapshotId, CancellationToken ct = default)
    {
        var snaps = await _db.Snapshots.OrderByDescending(s => s.WeekDate).ThenByDescending(s => s.Id).ToListAsync(ct);
        var sid = snapshotId ?? snaps.FirstOrDefault()?.Id ?? 0;

        // employees on this version that carry an occupation with no category → the review queue (by employee headcount)
        var unmapped = await _db.Employees
            .Where(e => e.SnapshotId == sid && e.OccupationRaw != null && (e.Occupation == null || e.Occupation.CategoryId == null))
            .GroupBy(e => e.OccupationRaw!)
            .Select(g => new UnmappedOccupation { Name = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToListAsync(ct);

        // employee counts per category (this version)
        var empByCat = await _db.Employees
            .Where(e => e.SnapshotId == sid && e.Occupation != null && e.Occupation.CategoryId != null)
            .GroupBy(e => e.Occupation!.CategoryId!.Value)
            .Select(g => new { CatId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CatId, x => x.Count, ct);

        // occupation counts per category (the catalog)
        var occByCat = await _db.Occupations
            .Where(o => o.CategoryId != null)
            .GroupBy(o => new { o.CategoryId, o.Category!.NameAr })
            .Select(g => new { g.Key.CategoryId, g.Key.NameAr, Occ = g.Count() })
            .ToListAsync(ct);

        ViewBag.GuideByCategory = occByCat
            .Select(x => new CatSummary { CategoryId = x.CategoryId!.Value, Name = x.NameAr, OccCount = x.Occ, EmpCount = empByCat.GetValueOrDefault(x.CategoryId!.Value, 0) })
            .OrderByDescending(x => x.EmpCount).ThenByDescending(x => x.OccCount).ToList();

        ViewBag.TotalEmp = await _db.Employees.CountAsync(e => e.SnapshotId == sid, ct);
        ViewBag.UnclassifiedEmp = await _db.Employees.CountAsync(e => e.SnapshotId == sid && (e.OccupationId == null || e.Occupation!.CategoryId == null), ct);

        ViewBag.Categories = await _db.Categories.OrderBy(c => c.NameAr).ToListAsync(ct);
        ViewBag.MappedCount = await _db.Occupations.CountAsync(o => o.CategoryId != null, ct);
        ViewBag.TotalOccupations = await _db.Occupations.CountAsync(ct);
        ViewBag.AllOccupations = await _db.Occupations
            .OrderBy(o => o.CategoryId != null).ThenBy(o => o.NameAr)
            .Select(o => new OccRow { Code = o.Code, NameAr = o.NameAr, CategoryId = o.CategoryId })
            .ToListAsync(ct);
        ViewBag.Snapshots = snaps;
        ViewBag.SnapshotId = sid;
        return View(unmapped);
    }

    // Drill-down: the employees behind a category, an occupation, or the whole Unclassified bucket (for a version).
    public async Task<IActionResult> Employees(int? categoryId, string? occupation, bool unclassified, int? snapshotId, CancellationToken ct = default)
    {
        var snaps = await _db.Snapshots.OrderByDescending(s => s.WeekDate).ThenByDescending(s => s.Id).ToListAsync(ct);
        var sid = snapshotId ?? snaps.FirstOrDefault()?.Id ?? 0;

        IQueryable<Employee> q = _db.Employees.Where(e => e.SnapshotId == sid);
        string title;
        if (!string.IsNullOrEmpty(occupation)) { q = q.Where(e => e.OccupationRaw == occupation); title = occupation!; }
        else if (categoryId is int cid) { q = q.Where(e => e.Occupation != null && e.Occupation.CategoryId == cid); title = (await _db.Categories.FindAsync(new object[] { cid }, ct))?.NameAr ?? ""; }
        else if (unclassified) { q = q.Where(e => e.OccupationId == null || e.Occupation!.CategoryId == null); title = "—"; }
        else return RedirectToAction(nameof(Index), new { snapshotId = sid });

        var list = await q
            .Select(e => new EmpRow
            {
                WorkerNo = e.WorkerNo,
                Name = e.Name,
                Nationality = e.Nationality != null ? e.Nationality.NameAr : null,
                IsSaudi = e.Nationality != null && e.Nationality.IsSaudi,
                Registration = e.Facility!.Record!.RegistrationLabel,
                Occupation = e.OccupationRaw
            })
            .OrderBy(x => x.Occupation).ThenBy(x => x.Name)
            .ToListAsync(ct);

        ViewBag.Heading = title;
        ViewBag.IsUnclassified = unclassified;
        ViewBag.Snapshots = snaps;
        ViewBag.SnapshotId = sid;
        return View(list);
    }

    // Assign EVERY currently-unmapped occupation to one category (new or existing) and back-fill employees.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MapAll(string? newCategory, int? categoryId, CancellationToken ct)
    {
        SaudizationCategory? cat = null;
        if (!string.IsNullOrWhiteSpace(newCategory))
        {
            var name = newCategory.Trim();
            cat = await _db.Categories.FirstOrDefaultAsync(c => c.NameAr == name, ct);
            if (cat is null) { cat = new SaudizationCategory { NameAr = name }; _db.Categories.Add(cat); }
        }
        else if (categoryId is int cid)
            cat = await _db.Categories.FindAsync(new object[] { cid }, ct);
        if (cat is null) { TempData["err"] = "Choose an existing category or type a new one."; return RedirectToAction(nameof(Index)); }

        var rawNames = await _db.Employees
            .Where(e => e.OccupationRaw != null && (e.Occupation == null || e.Occupation.CategoryId == null))
            .Select(e => e.OccupationRaw!).Distinct().ToListAsync(ct);

        int emps = 0;
        foreach (var raw in rawNames)
        {
            var nm = raw.Trim();
            var occ = await _db.Occupations.FirstOrDefaultAsync(o => o.NameAr == nm, ct);
            if (occ is null) { occ = new Occupation { NameAr = nm }; _db.Occupations.Add(occ); }
            occ.Category = cat;
            var linked = await _db.Employees.Where(e => e.OccupationRaw == raw && e.OccupationId == null).ToListAsync(ct);
            foreach (var e in linked) { e.Occupation = occ; emps++; }
        }
        await _db.SaveChangesAsync(ct);
        TempData["ok"] = $"Assigned {rawNames.Count} occupations to “{cat.NameAr}” ({emps} employees linked).";
        return RedirectToAction(nameof(Index));
    }

    // Map (or create) an occupation name to a category and back-fill matching employees.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Map(string occupationName, int categoryId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(occupationName)) return RedirectToAction(nameof(Index));

        var occ = await _db.Occupations.FirstOrDefaultAsync(o => o.NameAr == occupationName, ct);
        if (occ is null) { occ = new Occupation { NameAr = occupationName.Trim() }; _db.Occupations.Add(occ); }
        occ.CategoryId = categoryId;

        var emps = await _db.Employees.Where(e => e.OccupationRaw == occupationName && e.OccupationId == null).ToListAsync(ct);
        foreach (var e in emps) e.Occupation = occ;

        await _db.SaveChangesAsync(ct);
        TempData["ok"] = $"Mapped “{occupationName}” ({emps.Count} employees updated).";
        return RedirectToAction(nameof(Index));
    }

    // Bulk-apply an occupation-guide workbook (الرمز / المهنة / الفئة): upsert occupations + categories, re-link employees.
    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(32 * 1024 * 1024)]
    public async Task<IActionResult> ImportGuide(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) { TempData["err"] = "Please choose an .xlsx file."; return RedirectToAction(nameof(Index)); }
        try
        {
            await using var s = file.OpenReadStream();
            var res = await _importer.ApplyOccupationGuideAsync(s, ct);
            if (!res.Success) TempData["err"] = string.Join(" ", res.Warnings);
            else TempData["ok"] = $"Applied {res.GuideRows} rows: +{res.Categories} categories, +{res.Occupations} new occupations ({res.OccupationsUpdated} updated), {res.EmployeesRelinked} employees linked.";
        }
        catch (Exception ex) { TempData["err"] = "Import failed: " + ex.Message; }
        return RedirectToAction(nameof(Index));
    }
}

public class UnmappedOccupation
{
    public string Name { get; set; } = "";
    public int Count { get; set; }
}

public class OccRow
{
    public string? Code { get; set; }
    public string NameAr { get; set; } = "";
    public int? CategoryId { get; set; }
}

public class CatSummary
{
    public int CategoryId { get; set; }
    public string Name { get; set; } = "";
    public int OccCount { get; set; }
    public int EmpCount { get; set; }
}

public class EmpRow
{
    public string? WorkerNo { get; set; }
    public string Name { get; set; } = "";
    public string? Nationality { get; set; }
    public bool IsSaudi { get; set; }
    public string Registration { get; set; } = "";
    public string? Occupation { get; set; }
}
