using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Saudization.Web.Data;

namespace Saudization.Web.Controllers;

// Hosts the embedded org-chart browser (Chart) and HR hierarchy analysis (Analysis),
// adapted from the legacy webapp. Both are fed EMP_RAW built from a selected org version.
public class OrgViewController : Controller
{
    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly AppDbContext _db;
    public OrgViewController(AppDbContext db) => _db = db;

    public Task<IActionResult> Chart(int? versionId, CancellationToken ct = default) => Render("Chart", versionId, ct);
    public Task<IActionResult> Analysis(int? versionId, CancellationToken ct = default) => Render("Analysis", versionId, ct);

    private async Task<IActionResult> Render(string view, int? versionId, CancellationToken ct)
    {
        var versions = await _db.OrgVersions.OrderByDescending(v => v.Id).ToListAsync(ct);
        ViewBag.Versions = versions;
        if (versions.Count == 0) { ViewBag.EmpRaw = null; return View(view); }

        // default to the most recent version (versions is ordered by Id desc) — it carries the Job/position data,
        // unlike the very first "Original" import; an explicit versionId always wins.
        var vid = versionId ?? versions.First().Id;
        if (versions.All(v => v.Id != vid)) vid = versions.First().Id;
        ViewBag.VersionId = vid;
        ViewBag.VersionLabel = versions.First(v => v.Id == vid).Label;

        var divName = await _db.OrgDivisions.Where(x => x.OrgVersionId == vid)
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var dept = await _db.OrgDepartments.Where(x => x.OrgVersionId == vid)
            .Select(x => new { x.Id, x.Name, x.OrgDivisionId }).ToDictionaryAsync(x => x.Id, ct);
        var jobName = await _db.OrgJobs.Where(x => x.OrgVersionId == vid)
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
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
                branchCode = e.BranchCode ?? "",
                code = e.Code ?? "",
                nameEn = e.Name ?? "",
                hireDate = "", birthDate = "", idNumber = "",
                site = e.Site ?? "",
                division,
                department = d?.Name ?? "",
                section = "",
                costCenter = "", costCenterCode = "",
                position = position ?? "",
                nationality = e.Nationality ?? "", gender = "",
                managerCode = e.DirectManagerCode ?? ""
            };
        }).ToList();

        var payload = new { generatedAt = ViewBag.VersionLabel as string ?? "", sourceFile = "org", employees = rows };
        ViewBag.EmpRaw = JsonSerializer.Serialize(payload, Json);
        return View(view);
    }
}
