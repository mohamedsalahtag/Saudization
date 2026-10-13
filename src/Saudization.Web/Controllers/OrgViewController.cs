using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Saudization.Web.Data;
using Saudization.Web.Models;
using Saudization.Web.Services;

namespace Saudization.Web.Controllers;

// Hosts the embedded org-chart browser (Chart, fed EMP_RAW built from a selected org version) and the
// HR hierarchy analysis (Analysis, fed by OrgAnalysisService).
public class OrgViewController : Controller
{
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    // the analysis JSON goes inside a <script> block: keep Arabic readable but escape < > & so a name can't break out
    private static readonly JsonSerializerOptions SafeJson = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly AppDbContext _db;
    private readonly OrgAnalysisService _analysis;
    private readonly IStringLocalizer<SharedResource> _t;
    public OrgViewController(AppDbContext db, OrgAnalysisService analysis, IStringLocalizer<SharedResource> t) { _db = db; _analysis = analysis; _t = t; }

    private static bool Rtl => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

    public async Task<IActionResult> Chart(int? versionId, CancellationToken ct = default)
    {
        var versions = await _db.OrgVersions.OrderByDescending(v => v.Id).ToListAsync(ct);
        ViewBag.Versions = versions;
        if (versions.Count == 0) { ViewBag.EmpRaw = null; return View(); }

        // default to the most recent version — it carries the Job/position data; an explicit versionId always wins.
        var vid = versionId ?? versions.First().Id;
        if (versions.All(v => v.Id != vid)) vid = versions.First().Id;
        ViewBag.VersionId = vid;
        ViewBag.VersionLabel = versions.First(v => v.Id == vid).Label;

        var divName = await _db.OrgDivisions.Where(x => x.OrgVersionId == vid).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var dept = await _db.OrgDepartments.Where(x => x.OrgVersionId == vid).Select(x => new { x.Id, x.Name, x.OrgDivisionId }).ToDictionaryAsync(x => x.Id, ct);
        var jobName = await _db.OrgJobs.Where(x => x.OrgVersionId == vid).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var emps = await _db.OrgEmployees.Where(x => x.OrgVersionId == vid)
            .Select(x => new { x.Code, x.Name, x.BranchCode, x.Site, x.Nationality, x.DirectManagerCode, x.OrgDepartmentId, x.OrgJobId })
            .ToListAsync(ct);

        var rows = emps.Select(e =>
        {
            dept.TryGetValue(e.OrgDepartmentId, out var d);
            var division = d is not null && divName.TryGetValue(d.OrgDivisionId, out var dn) ? dn : "";
            var position = e.OrgJobId is int jid && jobName.TryGetValue(jid, out var jn) ? jn : "";
            return new
            {
                branchCode = e.BranchCode ?? "", code = e.Code ?? "", nameEn = e.Name ?? "",
                hireDate = "", birthDate = "", idNumber = "", site = e.Site ?? "", division, department = d?.Name ?? "", section = "",
                costCenter = "", costCenterCode = "", position = position ?? "", nationality = e.Nationality ?? "", gender = "",
                managerCode = e.DirectManagerCode ?? ""
            };
        }).ToList();

        // head-of-department reference: the HOD sheet imported with this version (or the latest one), else the legacy global rows
        var heads = (await _analysis.LoadHeadsAsync(vid, ct))
            .Select(h => new { division = h.Division, department = h.Department, headCode = h.HeadCode, headName = h.HeadName }).ToList();

        var payload = new { generatedAt = ViewBag.VersionLabel as string ?? "", sourceFile = "org", employees = rows, heads };
        ViewBag.EmpRaw = JsonSerializer.Serialize(payload, Json);
        return View();
    }

    public async Task<IActionResult> Analysis(int? versionId, string? branch, string? site, string? division, CancellationToken ct = default)
    {
        var vid = await ResolveVersionAsync(versionId, ct);
        if (vid is null) return View(null);
        var result = await _analysis.BuildAsync(vid.Value, Clean(branch), Clean(site), Clean(division), k => _t[k].Value, ct);
        ViewBag.ResultJson = JsonSerializer.Serialize(result, SafeJson);
        return View(result);
    }

    // kind: all | departments | managers | changes | finding (key = finding id) | fixlist (key = "division|department")
    public async Task<IActionResult> AnalysisExport(int versionId, string kind, string? key, string? format, string? branch, string? site, string? division, CancellationToken ct = default)
    {
        var vid = await ResolveVersionAsync(versionId, ct);
        if (vid is null) return NotFound();
        var r = await _analysis.BuildAsync(vid.Value, Clean(branch), Clean(site), Clean(division), k => _t[k].Value, ct);
        var stamp = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var scope = string.Join("_", new[] { branch, site, division }.Where(s => !string.IsNullOrWhiteSpace(s))!.Select(Slug));
        string Name(string n) => n + (scope.Length > 0 ? "_" + scope : "") + "_" + stamp;
        switch ((kind ?? "").ToLowerInvariant())
        {
            case "all": return File(OrgAnalysisExport.All(r, _t["OrgA_UI_Summary"].Value, Rtl), Xlsx, Name("hierarchy_findings") + ".xlsx");
            case "departments": return File(OrgAnalysisExport.Workbook(new[] { new OrgAnalysisExport.Sheet(r.Strings["D_dept"], OrgAnalysisExport.DeptCols(r), OrgAnalysisExport.DeptRows(r)) }, Rtl), Xlsx, Name("departments") + ".xlsx");
            case "managers": return File(OrgAnalysisExport.Workbook(new[] { new OrgAnalysisExport.Sheet(r.Strings["M_name"], OrgAnalysisExport.MgrCols(r), OrgAnalysisExport.MgrRows(r)) }, Rtl), Xlsx, Name("managers") + ".xlsx");
            case "changes": return File(OrgAnalysisExport.Changes(r, Rtl), Xlsx, Name("changes") + ".xlsx");
            case "fixlist":
                var d = r.Depts.FirstOrDefault(x => x.Key == key);
                if (d is null) return NotFound();
                return File(OrgAnalysisExport.FixList(r, d, Rtl), Xlsx, Name("fixlist_" + Slug(d.Dept)) + ".xlsx");
            case "finding":
                var f = r.Finding(key ?? "");
                if (f is null) return NotFound();
                if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
                    return File(OrgAnalysisExport.Csv(f.Columns, f.Rows), "text/csv; charset=utf-8", Name(f.Id) + ".csv");
                return File(OrgAnalysisExport.Workbook(new[] { new OrgAnalysisExport.Sheet(f.Title, f.Columns, f.Rows) }, Rtl), Xlsx, Name(f.Id) + ".xlsx");
            default: return BadRequest("Unknown export.");
        }
    }

    public class AcceptPayload { public string FindingId { get; set; } = ""; public string RowKey { get; set; } = ""; public string? RowLabel { get; set; } public bool Accept { get; set; } }

    // Mark a finding row as expected (or undo). Global, so it stays accepted on later uploads; the audit log records it.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Accept([FromBody] AcceptPayload p, CancellationToken ct)
    {
        if (p is null || string.IsNullOrWhiteSpace(p.FindingId) || string.IsNullOrWhiteSpace(p.RowKey)) return BadRequest();
        var fid = p.FindingId.Trim(); var key = p.RowKey.Trim();
        var existing = await _db.OrgFindingAccepts.FirstOrDefaultAsync(a => a.FindingId == fid && a.RowKey == key, ct);
        if (p.Accept && existing is null)
            _db.OrgFindingAccepts.Add(new OrgFindingAccept { FindingId = fid, RowKey = key, RowLabel = p.RowLabel?.Trim() is { Length: > 0 } l ? l[..Math.Min(300, l.Length)] : null, AcceptedByUser = User?.Identity?.Name ?? "web", AcceptedAtUtc = DateTime.UtcNow });
        else if (!p.Accept && existing is not null)
            _db.OrgFindingAccepts.Remove(existing);
        await _db.SaveChangesAsync(ct);
        return Json(new { ok = true, accepted = p.Accept });
    }

    private async Task<int?> ResolveVersionAsync(int? versionId, CancellationToken ct)
    {
        var versions = await _db.OrgVersions.OrderByDescending(v => v.Id).Select(v => v.Id).ToListAsync(ct);
        if (versions.Count == 0) return null;
        return versionId is int v && versions.Contains(v) ? v : versions[0];
    }
    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static string Slug(string? s) => System.Text.RegularExpressions.Regex.Replace(s ?? "", @"[^\w؀-ۿ-]+", "-").Trim('-');
}
