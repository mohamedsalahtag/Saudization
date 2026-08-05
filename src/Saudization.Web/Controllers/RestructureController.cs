using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Saudization.Web.Data;
using Saudization.Web.Models;
using Saudization.Web.Services;

namespace Saudization.Web.Controllers;

public class RestructureController : Controller
{
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private readonly AppDbContext _db;
    private readonly OrgImporter _importer;
    private readonly OrgTemplateService _template;
    public RestructureController(AppDbContext db, OrgImporter importer, OrgTemplateService template)
    { _db = db; _importer = importer; _template = template; }

    public async Task<IActionResult> Index(int? versionId, bool divisionOnly = false, CancellationToken ct = default)
    {
        var versions = await _db.OrgVersions.OrderByDescending(v => v.Id).ToListAsync(ct);
        var vm = new RestructureViewModel { Versions = versions };
        if (versions.Count == 0) return View(vm);

        var vid = versionId ?? versions.First(v => v.IsOriginal || true).Id;
        if (versions.All(v => v.Id != vid)) vid = versions.First().Id;

        vm.HasData = true;
        vm.Model = await LoadModelAsync(vid, ct);
        vm.Consistency = OrgConsistency.Analyze(vm.Model, divisionOnly);
        ViewBag.DivisionOnly = divisionOnly;
        return View(vm);
    }

    public async Task<IActionResult> Inconsistent(int versionId, bool divisionOnly = false, CancellationToken ct = default)
    {
        var model = await LoadModelAsync(versionId, ct);
        ViewBag.DivisionOnly = divisionOnly;
        ViewBag.VersionId = versionId;
        return View(OrgConsistency.Analyze(model, divisionOnly));
    }

    public async Task<IActionResult> Compare(int versionId, int? baseId, CancellationToken ct = default)
    {
        var versions = await _db.OrgVersions.OrderBy(v => v.Id).ToListAsync(ct);
        var bId = baseId ?? versions.FirstOrDefault(v => v.IsOriginal)?.Id
                          ?? (await _db.OrgVersions.FindAsync(new object[] { versionId }, ct))?.SourceVersionId
                          ?? versionId;
        var cur = await LoadModelAsync(versionId, ct);
        var bas = await LoadModelAsync(bId, ct);

        Dictionary<string, (string div, string dept, string job)> Resolve(OrgModelDto m)
        {
            var dv = m.Divisions.ToDictionary(x => x.Id, x => x.Name);
            var dp = m.Departments.ToDictionary(x => x.Id, x => (x.Name, x.DivisionId));
            var jb = m.Jobs.ToDictionary(x => x.Id, x => x.Name);
            var map = new Dictionary<string, (string, string, string)>();
            foreach (var e in m.Employees)
            {
                if (string.IsNullOrWhiteSpace(e.Code)) continue;
                dp.TryGetValue(e.DepartmentId, out var d);
                var div = dv.TryGetValue(d.DivisionId, out var dn) ? dn : "";
                var job = e.JobId is int j && jb.TryGetValue(j, out var jn) ? jn : "";
                map[e.Code!.Trim()] = (div, d.Name ?? "", job);
            }
            return map;
        }
        var a = Resolve(bas); var b = Resolve(cur);
        var rows = new List<OrgMoveRow>();
        var nameByCode = new Dictionary<string, string>();
        foreach (var e in cur.Employees) if (!string.IsNullOrWhiteSpace(e.Code)) nameByCode[e.Code!.Trim()] = e.Name;
        foreach (var kv in b)
        {
            if (!a.TryGetValue(kv.Key, out var old)) continue;
            var n = kv.Value;
            if (old.div == n.div && old.dept == n.dept && old.job == n.job) continue;
            rows.Add(new OrgMoveRow
            {
                Code = kv.Key, Name = nameByCode.GetValueOrDefault(kv.Key, ""),
                FromDivision = old.div, ToDivision = n.div, FromDepartment = old.dept, ToDepartment = n.dept,
                FromJob = old.job, ToJob = n.job
            });
        }
        ViewBag.Versions = versions; ViewBag.VersionId = versionId; ViewBag.BaseId = bId;
        ViewBag.CurLabel = versions.FirstOrDefault(v => v.Id == versionId)?.Label;
        ViewBag.BaseLabel = versions.FirstOrDefault(v => v.Id == bId)?.Label;
        return View(rows.OrderBy(r => r.Name).ToList());
    }

    public IActionResult Import() => View();

    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(64 * 1024 * 1024)]
    public async Task<IActionResult> Import(IFormFile? file, string? label, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0) { TempData["err"] = "Please choose an .xlsx file."; return RedirectToAction(nameof(Import)); }
        try
        {
            await using var s = file.OpenReadStream();
            var res = await _importer.ImportAsync(s, string.IsNullOrWhiteSpace(label) ? "Original " + DateTime.UtcNow.ToString("yyyy-MM-dd") : label!, User?.Identity?.Name, ct);
            TempData["ok"] = $"Imported {res.Employees} employees, {res.Divisions} divisions, {res.Departments} departments.";
            return RedirectToAction(nameof(Index), new { versionId = res.VersionId });
        }
        catch (Exception ex) { TempData["err"] = "Import failed: " + ex.Message; return RedirectToAction(nameof(Import)); }
    }

    // Persist an edited model (posted as JSON) as a NEW version; the original stays untouched.
    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(64 * 1024 * 1024)]
    public async Task<IActionResult> Save(string payload, string? label, CancellationToken ct = default)
    {
        OrgModelDto? m;
        try { m = System.Text.Json.JsonSerializer.Deserialize<OrgModelDto>(payload); }
        catch (Exception ex) { return BadRequest(ex.Message); }
        if (m is null || m.Departments.Count == 0) return BadRequest("Empty model.");

        var version = new OrgVersion
        {
            Label = string.IsNullOrWhiteSpace(label) ? "Edited " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm") : label!.Trim(),
            IsOriginal = false,
            SourceVersionId = m.VersionId,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUser = User?.Identity?.Name ?? "web"
        };
        _db.OrgVersions.Add(version);

        var divMap = new Dictionary<int, OrgDivision>();
        foreach (var d in m.Divisions)
        { var e = new OrgDivision { Name = d.Name }; version.Divisions.Add(e); divMap[d.Id] = e; }

        var deptMap = new Dictionary<int, OrgDepartment>();
        foreach (var d in m.Departments)
        {
            if (!divMap.TryGetValue(d.DivisionId, out var div)) continue;
            var e = new OrgDepartment { Name = d.Name, Division = div }; version.Departments.Add(e); deptMap[d.Id] = e;
        }

        var jobMap = new Dictionary<int, OrgJob>();
        foreach (var j in m.Jobs)
        {
            if (!deptMap.TryGetValue(j.DepartmentId, out var dep)) continue;
            var e = new OrgJob { Name = j.Name, Department = dep }; version.Jobs.Add(e); jobMap[j.Id] = e;
        }

        foreach (var emp in m.Employees)
        {
            if (!deptMap.TryGetValue(emp.DepartmentId, out var dep)) continue;
            version.Employees.Add(new OrgEmployee
            {
                Code = emp.Code, Name = emp.Name, Site = emp.Site, DirectManagerCode = emp.ManagerCode,
                Department = dep, Job = emp.JobId is int jid && jobMap.TryGetValue(jid, out var jb) ? jb : null
            });
        }

        await _db.SaveChangesAsync(ct);
        return Json(new { versionId = version.Id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteVersion(int id, CancellationToken ct)
    {
        // ordered delete (all FKs are NoAction)
        await _db.OrgEmployees.Where(x => x.OrgVersionId == id).ExecuteDeleteAsync(ct);
        await _db.OrgJobs.Where(x => x.OrgVersionId == id).ExecuteDeleteAsync(ct);
        await _db.OrgDepartments.Where(x => x.OrgVersionId == id).ExecuteDeleteAsync(ct);
        await _db.OrgDivisions.Where(x => x.OrgVersionId == id).ExecuteDeleteAsync(ct);
        await _db.OrgVersions.Where(x => x.Id == id).ExecuteDeleteAsync(ct);
        TempData["ok"] = "Version deleted.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Template(int? versionId, CancellationToken ct = default)
    {
        var bytes = await _template.BuildAsync(versionId, ct);
        return File(bytes, Xlsx, "org-template.xlsx");
    }

    public async Task<IActionResult> Export(int versionId, CancellationToken ct = default)
    {
        var bytes = await _template.BuildAsync(versionId, ct);
        var v = await _db.OrgVersions.FindAsync(new object[] { versionId }, ct);
        return File(bytes, Xlsx, $"org-{v?.Label ?? versionId.ToString()}.xlsx");
    }

    private async Task<OrgModelDto> LoadModelAsync(int vid, CancellationToken ct)
    {
        var v = await _db.OrgVersions.FindAsync(new object[] { vid }, ct);
        return new OrgModelDto
        {
            VersionId = vid,
            Label = v?.Label ?? "",
            IsOriginal = v?.IsOriginal ?? false,
            Divisions = await _db.OrgDivisions.Where(x => x.OrgVersionId == vid)
                .Select(x => new OrgDivisionDto { Id = x.Id, Name = x.Name }).ToListAsync(ct),
            Departments = await _db.OrgDepartments.Where(x => x.OrgVersionId == vid)
                .Select(x => new OrgDeptDto { Id = x.Id, Name = x.Name, DivisionId = x.OrgDivisionId }).ToListAsync(ct),
            Jobs = await _db.OrgJobs.Where(x => x.OrgVersionId == vid)
                .Select(x => new OrgJobDto { Id = x.Id, Name = x.Name, DepartmentId = x.OrgDepartmentId }).ToListAsync(ct),
            Employees = await _db.OrgEmployees.Where(x => x.OrgVersionId == vid)
                .Select(x => new OrgEmpDto { Id = x.Id, Code = x.Code, Name = x.Name, Site = x.Site, ManagerCode = x.DirectManagerCode, DepartmentId = x.OrgDepartmentId, JobId = x.OrgJobId }).ToListAsync(ct),
        };
    }
}
