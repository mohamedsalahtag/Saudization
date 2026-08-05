using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Saudization.Web.Data;

namespace Saudization.Web.Services;

/// <summary>Builds a masterdata-shaped workbook (with an added Job column), pre-filled with a version's data.</summary>
public class OrgTemplateService
{
    private readonly AppDbContext _db;
    public OrgTemplateService(AppDbContext db) => _db = db;

    private static readonly string[] Headers =
    { "Branch Code", "Employees code", "Name of Employee", "Site Name", "Nationality", "Division", "Department", "Job", "Direct Manager Code" };

    public async Task<byte[]> BuildAsync(int? versionId, CancellationToken ct = default)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Sheet2");
        for (int i = 0; i < Headers.Length; i++) ws.Cell(1, i + 1).Value = Headers[i];
        ws.Row(1).Style.Font.Bold = true;

        if (versionId is int vid)
        {
            var divName = await _db.OrgDivisions.Where(x => x.OrgVersionId == vid).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
            var depts = await _db.OrgDepartments.Where(x => x.OrgVersionId == vid).ToDictionaryAsync(x => x.Id, x => new { x.Name, x.OrgDivisionId }, ct);
            var jobName = await _db.OrgJobs.Where(x => x.OrgVersionId == vid).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
            var emps = await _db.OrgEmployees.Where(x => x.OrgVersionId == vid).OrderBy(x => x.Name).ToListAsync(ct);

            int r = 2;
            foreach (var e in emps)
            {
                depts.TryGetValue(e.OrgDepartmentId, out var d);
                var div = d != null && divName.TryGetValue(d.OrgDivisionId, out var dn) ? dn : "";
                ws.Cell(r, 1).Value = e.BranchCode; ws.Cell(r, 2).Value = e.Code; ws.Cell(r, 3).Value = e.Name;
                ws.Cell(r, 4).Value = e.Site; ws.Cell(r, 5).Value = e.Nationality; ws.Cell(r, 6).Value = div; ws.Cell(r, 7).Value = d?.Name;
                ws.Cell(r, 8).Value = e.OrgJobId is int jid && jobName.TryGetValue(jid, out var jn) ? jn : "";
                ws.Cell(r, 9).Value = e.DirectManagerCode;
                r++;
            }
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
