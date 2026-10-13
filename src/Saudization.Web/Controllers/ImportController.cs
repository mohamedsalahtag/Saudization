using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Saudization.Web.Data;
using Saudization.Web.Models;
using Saudization.Web.Services;

namespace Saudization.Web.Controllers;

public class ImportController : Controller
{
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private readonly ExcelImporter _importer;
    private readonly ExcelTemplateService _template;
    private readonly AppDbContext _db;
    private readonly SnapshotComparer _comparer;
    private readonly PendingUploadStore _pending;
    public ImportController(ExcelImporter importer, ExcelTemplateService template, AppDbContext db, SnapshotComparer comparer, PendingUploadStore pending)
    { _importer = importer; _template = template; _db = db; _comparer = comparer; _pending = pending; }

    public IActionResult Index()
    {
        var today = DateTime.UtcNow.Date;
        var monday = today.AddDays(-(int)(today.DayOfWeek == DayOfWeek.Sunday ? 6 : (int)today.DayOfWeek - 1));
        ViewBag.DefaultWeek = monday.ToString("yyyy-MM-dd");
        ViewBag.DefaultLabel = IsoWeekLabel(monday);
        return View();
    }

    // Step 1 — upload: the file is kept on the server and read WITHOUT saving, so the user can check it first.
    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(64 * 1024 * 1024)]
    public async Task<IActionResult> Index(IFormFile? file, string? label, DateTime? weekDate, string? note,
        ImportSheetRule rule = ImportSheetRule.PerFacility, bool updateMaster = false, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
        {
            TempData["err"] = "Please choose an .xlsx file.";
            return RedirectToAction(nameof(Index));
        }
        var week = (weekDate ?? DateTime.UtcNow).Date;
        var meta = new PendingUploadMeta
        {
            Label = string.IsNullOrWhiteSpace(label) ? IsoWeekLabel(week) : label!.Trim(),
            WeekDate = week, Note = note, Rule = rule, UpdateMaster = updateMaster,
            User = CurrentUser, FileName = Path.GetFileName(file.FileName), CreatedUtc = DateTime.UtcNow
        };
        var token = await _pending.SaveAsync(file, meta, ct);
        try
        {
            var previousDefault = await _db.DefaultSnapshotIdAsync(ct);
            var snapshot = NewSnapshot(meta);
            _db.Snapshots.Add(snapshot);
            ImportResult res;
            await using (var s = _pending.Open(token))
                res = await _importer.ImportAsync(s, snapshot, rule, protectMaster: !updateMaster, ct: ct, save: false);
            if (!res.Success)
            {
                _db.ChangeTracker.Clear();
                _pending.Delete(token);
                TempData["err"] = "This file can't be imported — " + string.Join(" ", res.Warnings.Where(w => w.StartsWith("ERROR")).Select(w => w[6..].Trim()));
                return RedirectToAction(nameof(Index));
            }

            // what the upload would add, described from the not-yet-saved rows — then discard them
            var rows = SnapshotComparer.FromEmployees(_db.ChangeTracker.Entries<Employee>()
                .Select(e => e.Entity).Where(e => ReferenceEquals(e.Snapshot, snapshot)));
            _db.ChangeTracker.Clear();

            var vm = new ImportPreviewViewModel
            {
                Token = token, Meta = meta, Result = res, Employees = rows.Count,
                Saudi = rows.Count(r => r.Saudi || r.Special),
                Identical = await _comparer.FindIdenticalAsync(SnapshotComparer.Hash(rows), ct: ct),
                SameWeek = await _db.Snapshots.Where(x => x.WeekDate == week).Select(x => x.Label).ToListAsync(ct)
            };
            if (previousDefault > 0)
            {
                var label0 = await _db.Snapshots.Where(x => x.Id == previousDefault).Select(x => x.Label).FirstAsync(ct);
                vm.Diff = SnapshotComparer.Diff(rows, await _comparer.LoadAsync(previousDefault, ct), previousDefault, label0);
            }
            // suggest "make default" unless nobody joined or left (usually an old file uploaded by mistake)
            vm.SuggestDefault = vm.Diff is null || !vm.Diff.NoPeopleChange;
            return View("Preview", vm);
        }
        catch (Exception ex)
        {
            _db.ChangeTracker.Clear();
            _pending.Delete(token);
            TempData["err"] = "Import failed: " + ex.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    // Step 2 — confirm: import the kept file for real.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(string token, bool makeDefault = false, CancellationToken ct = default)
    {
        var meta = _pending.Load(token);
        if (meta is null) { TempData["err"] = "This upload has expired or was already handled — please upload the file again."; return RedirectToAction(nameof(Index)); }
        if (!string.Equals(meta.User, CurrentUser, StringComparison.OrdinalIgnoreCase)) return Forbid();
        try
        {
            var previousDefault = await _db.DefaultSnapshotIdAsync(ct);
            var snapshot = NewSnapshot(meta);
            _db.Snapshots.Add(snapshot);
            ImportResult res;
            await using (var s = _pending.Open(token))
                res = await _importer.ImportAsync(s, snapshot, meta.Rule, protectMaster: !meta.UpdateMaster, ct: ct);
            if (!res.Success)
            {
                TempData["err"] = "Import failed — nothing was saved. " + string.Join(" ", res.Warnings.Where(w => w.StartsWith("ERROR")).Select(w => w[6..].Trim()));
                return RedirectToAction(nameof(Index));
            }
            // re-check: an identical version may have been saved since the preview
            var identical = await _comparer.FindIdenticalAsync(await _comparer.EnsureHashAsync(snapshot.Id, ct), snapshot.Id, ct);
            if (identical is not null)
            {
                await _db.Snapshots.IgnoreQueryFilters().Where(x => x.Id == snapshot.Id).ExecuteDeleteAsync(ct);
                _pending.Delete(token);
                TempData["err"] = $"Import not saved: the file contains exactly the same employees as version “{identical.Label}”.";
                return RedirectToAction(nameof(Index));
            }
            if (makeDefault)
            {
                await _db.SetDefaultSnapshotAsync(snapshot.Id, ct);
                HttpContext.ClearSelectedSnapshot();              // drop any remembered selection so it shows everywhere
            }
            _pending.Delete(token);
            ViewBag.Snapshot = snapshot;
            ViewBag.Diff = previousDefault > 0 ? await _comparer.DiffAsync(snapshot.Id, previousDefault, ct) : null;
            ViewBag.MadeDefault = makeDefault;
            return View("Result", res);
        }
        catch (Exception ex)
        {
            TempData["err"] = "Import failed: " + ex.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Cancel(string token)
    {
        _pending.Delete(token);
        TempData["ok"] = "Upload cancelled — nothing was saved.";
        return RedirectToAction(nameof(Index));
    }

    private string CurrentUser => User?.Identity?.Name ?? "web";

    private Snapshot NewSnapshot(PendingUploadMeta m) => new()
    {
        Label = m.Label, WeekDate = m.WeekDate, Note = m.Note,
        CreatedAtUtc = DateTime.UtcNow, CreatedByUser = CurrentUser
    };

    // Download a ready-to-fill workbook matching the chosen sheet rule.
    // mode: "data" = filled with current data · "empty" = headers only · "sample" = headers + up to 5 example rows.
    public async Task<IActionResult> Template(ImportSheetRule rule = ImportSheetRule.PerFacility, string mode = "data", CancellationToken ct = default)
    {
        int limit = mode switch { "empty" => 0, "sample" => 5, _ => -1 };
        var bytes = await _template.BuildAsync(rule, limit, ct);
        var kind = rule == ImportSheetRule.PerRegistration ? "by-registration" : "by-facility";
        var tag = mode switch { "empty" => "empty", "sample" => "sample", _ => "with-data" };
        return File(bytes, Xlsx, $"saudization-template-{kind}-{tag}.xlsx");
    }

    private static string IsoWeekLabel(DateTime d)
    {
        var week = System.Globalization.ISOWeek.GetWeekOfYear(d);
        return $"{d:yyyy}-W{week:00}";
    }
}
