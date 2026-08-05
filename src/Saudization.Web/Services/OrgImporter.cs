using ClosedXML.Excel;
using Saudization.Web.Data;
using Saudization.Web.Models;

namespace Saudization.Web.Services;

public class OrgImportResult
{
    public int VersionId;
    public int Divisions, Departments, Jobs, Employees;
    public readonly List<string> Warnings = new();
}

/// <summary>
/// Parses a masterdata.xlsx-shaped sheet into a new "original" OrgVersion. Departments are noded per
/// (Division, Department) pair, so the current messy state (a department under several divisions) is
/// preserved for the HR manager to consolidate.
/// </summary>
public class OrgImporter
{
    private readonly AppDbContext _db;
    public OrgImporter(AppDbContext db) => _db = db;

    private const string NoDiv = "(No Division)";
    private const string NoDept = "(No Department)";

    public async Task<OrgImportResult> ImportAsync(Stream xlsx, string label, string? user, CancellationToken ct = default)
    {
        var res = new OrgImportResult();
        using var wb = new XLWorkbook(xlsx);
        var ws = wb.Worksheets.FirstOrDefault(w => w.Name.Equals("Sheet2", StringComparison.OrdinalIgnoreCase))
                 ?? wb.Worksheets.First();

        var h = HeaderIndex(ws);
        int cBranch = Col(h, "branch code", "branch");
        int cCode = Col(h, "employees code", "employee code", "employee");
        int cName = Col(h, "name of employee", "employee name", "name");
        int cSite = Col(h, "site name", "site");
        int cDiv = Col(h, "division");
        int cDept = Col(h, "department");
        int cJob = Col(h, "job", "position");           // optional
        int cNat = Col(h, "nationality", "الجنسية");     // optional
        int cMgr = Col(h, "direct manager code", "manager code", "manager");

        var version = new OrgVersion
        {
            Label = string.IsNullOrWhiteSpace(label) ? "Original" : label.Trim(),
            IsOriginal = true,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUser = user
        };
        _db.OrgVersions.Add(version);

        var divs = new Dictionary<string, OrgDivision>();
        var depts = new Dictionary<(int, string), OrgDepartment>();   // (divisionRef, deptName) -> node
        var jobs = new Dictionary<(int, string), OrgJob>();

        OrgDivision GetDiv(string name)
        {
            name = string.IsNullOrWhiteSpace(name) ? NoDiv : name.Trim();
            if (!divs.TryGetValue(name, out var d))
            {
                d = new OrgDivision { Name = name }; version.Divisions.Add(d); divs[name] = d; res.Divisions++;
            }
            return d;
        }
        OrgDepartment GetDept(OrgDivision div, string name)
        {
            name = string.IsNullOrWhiteSpace(name) ? NoDept : name.Trim();
            var key = (RuntimeId(div), name);
            if (!depts.TryGetValue(key, out var d))
            {
                d = new OrgDepartment { Name = name, Division = div }; version.Departments.Add(d); depts[key] = d; res.Departments++;
            }
            return d;
        }
        OrgJob GetJob(OrgDepartment dept, string name)
        {
            var key = (RuntimeId(dept), name.Trim());
            if (!jobs.TryGetValue(key, out var j))
            {
                j = new OrgJob { Name = name.Trim(), Department = dept }; version.Jobs.Add(j); jobs[key] = j; res.Jobs++;
            }
            return j;
        }

        foreach (var row in DataRows(ws))
        {
            var name = Str(row, cName);
            var code = Str(row, cCode);
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(code)) continue;

            var div = GetDiv(Str(row, cDiv) ?? "");
            var dept = GetDept(div, Str(row, cDept) ?? "");
            OrgJob? job = null;
            var jobName = Str(row, cJob);
            if (!string.IsNullOrWhiteSpace(jobName) && !jobName!.Equals("NA", StringComparison.OrdinalIgnoreCase))
                job = GetJob(dept, jobName!);

            version.Employees.Add(new OrgEmployee
            {
                Code = code,
                Name = name ?? code ?? "",
                BranchCode = Str(row, cBranch),
                Site = Str(row, cSite),
                Nationality = Str(row, cNat),
                DirectManagerCode = Str(row, cMgr),
                Department = dept,
                Job = job
            });
            res.Employees++;
        }

        await _db.SaveChangesAsync(ct);
        res.VersionId = version.Id;
        return res;
    }

    // reference identity for keying transient nodes before they get DB ids
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, object> _ids = new();
    private static int _seq;
    private static int RuntimeId(object o)
    {
        if (_ids.TryGetValue(o, out var v)) return (int)v;
        var id = System.Threading.Interlocked.Increment(ref _seq);
        _ids.Add(o, id); return id;
    }

    // ---- helpers ----
    private static Dictionary<string, int> HeaderIndex(IXLWorksheet ws)
    {
        var dict = new Dictionary<string, int>();
        var header = ws.RowsUsed().FirstOrDefault();
        if (header is null) return dict;
        foreach (var cell in header.CellsUsed())
        {
            var key = Norm(cell.GetString());
            if (key.Length > 0 && !dict.ContainsKey(key)) dict[key] = cell.Address.ColumnNumber;
        }
        return dict;
    }
    private static int Col(Dictionary<string, int> h, params string[] cands)
    {
        foreach (var c in cands)
        {
            var key = Norm(c);
            if (h.TryGetValue(key, out var i)) return i;
            foreach (var kv in h) if (kv.Key.Contains(key) || key.Contains(kv.Key)) return kv.Value;
        }
        return -1;
    }
    private static IEnumerable<IXLRangeRow> DataRows(IXLWorksheet ws)
    {
        var used = ws.RangeUsed(); if (used is null) yield break;
        bool first = true;
        foreach (var row in used.RowsUsed()) { if (first) { first = false; continue; } yield return row; }
    }
    private static string? Str(IXLRangeRow row, int col)
    {
        if (col < 1) return null;
        var v = row.Cell(col).GetString().Trim();
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }
    private static string Norm(string s) => System.Text.RegularExpressions.Regex.Replace((s ?? "").Trim().ToLowerInvariant(), @"\s+", " ");
}
