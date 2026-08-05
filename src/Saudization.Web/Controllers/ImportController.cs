using Microsoft.AspNetCore.Mvc;
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
    public ImportController(ExcelImporter importer, ExcelTemplateService template, AppDbContext db)
    { _importer = importer; _template = template; _db = db; }

    public IActionResult Index()
    {
        var today = DateTime.UtcNow.Date;
        var monday = today.AddDays(-(int)(today.DayOfWeek == DayOfWeek.Sunday ? 6 : (int)today.DayOfWeek - 1));
        ViewBag.DefaultWeek = monday.ToString("yyyy-MM-dd");
        ViewBag.DefaultLabel = IsoWeekLabel(monday);
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(64 * 1024 * 1024)]
    public async Task<IActionResult> Index(IFormFile? file, string? label, DateTime? weekDate, string? note,
        ImportSheetRule rule = ImportSheetRule.PerFacility, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
        {
            TempData["err"] = "Please choose an .xlsx file.";
            return RedirectToAction(nameof(Index));
        }
        var week = (weekDate ?? DateTime.UtcNow).Date;
        var snapshot = new Snapshot
        {
            Label = string.IsNullOrWhiteSpace(label) ? IsoWeekLabel(week) : label!.Trim(),
            WeekDate = week,
            Note = note,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUser = User?.Identity?.Name ?? "web"
        };
        try
        {
            _db.Snapshots.Add(snapshot);
            await using var s = file.OpenReadStream();
            var res = await _importer.ImportAsync(s, snapshot, rule, ct);
            ViewBag.Snapshot = snapshot;
            return View("Result", res);
        }
        catch (Exception ex)
        {
            TempData["err"] = "Import failed: " + ex.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    // Download a ready-to-fill workbook matching the chosen sheet rule.
    public async Task<IActionResult> Template(ImportSheetRule rule = ImportSheetRule.PerFacility, CancellationToken ct = default)
    {
        var bytes = await _template.BuildAsync(rule, ct);
        var name = rule == ImportSheetRule.PerRegistration ? "saudization-template-by-registration.xlsx" : "saudization-template-by-facility.xlsx";
        return File(bytes, Xlsx, name);
    }

    private static string IsoWeekLabel(DateTime d)
    {
        var week = System.Globalization.ISOWeek.GetWeekOfYear(d);
        return $"{d:yyyy}-W{week:00}";
    }
}
