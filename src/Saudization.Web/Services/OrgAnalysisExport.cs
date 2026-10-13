using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace Saudization.Web.Services;

/// <summary>Excel / CSV output for the HR hierarchy analysis — one sheet per list, Arabic-safe, built from the
/// same result the page shows.</summary>
public static class OrgAnalysisExport
{
    public record Sheet(string Name, List<OrgCol> Cols, IEnumerable<Dictionary<string, object?>> Rows);
    private static readonly Regex Tags = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex BadSheetChars = new(@"[\[\]:*?/\\]", RegexOptions.Compiled);

    public static byte[] Workbook(IEnumerable<Sheet> sheets, bool rtl)
    {
        using var wb = new XLWorkbook();
        var used = new HashSet<string>();
        foreach (var s in sheets)
        {
            var name = BadSheetChars.Replace(s.Name, " ").Trim(); if (name.Length > 31) name = name[..31]; if (name.Length == 0) name = "Sheet";
            var baseName = name; int n = 2;
            while (!used.Add(name)) { var suffix = " (" + n++ + ")"; name = baseName[..Math.Min(baseName.Length, 31 - suffix.Length)] + suffix; }
            var ws = wb.Worksheets.Add(name);
            ws.RightToLeft = rtl;
            var cols = s.Cols.Where(c => !c.Key.StartsWith("_")).ToList();
            bool anyAccepted = s.Rows.Any(OrgFinding.IsAccepted);
            for (int i = 0; i < cols.Count; i++) ws.Cell(1, i + 1).Value = cols[i].Label;
            if (anyAccepted) ws.Cell(1, cols.Count + 1).Value = "Accepted";
            ws.Row(1).Style.Font.Bold = true;
            int r = 2;
            foreach (var row in s.Rows)
            {
                for (int i = 0; i < cols.Count; i++) Set(ws.Cell(r, i + 1), row.TryGetValue(cols[i].Key, out var v) ? v : null);
                if (anyAccepted) ws.Cell(r, cols.Count + 1).Value = OrgFinding.IsAccepted(row) ? "yes" : "";
                r++;
            }
            ws.SheetView.FreezeRows(1);
            if (r > 2) ws.Range(1, 1, r - 1, cols.Count + (anyAccepted ? 1 : 0)).SetAutoFilter();
            ws.Columns().AdjustToContents(1, Math.Min(r, 200));
            foreach (var c in ws.ColumnsUsed()) if (c.Width > 60) c.Width = 60;
        }
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void Set(IXLCell cell, object? v)
    {
        switch (v)
        {
            case null: break;
            case string s: cell.Value = Tags.Replace(s, ""); break;
            case int i: cell.Value = i; break;
            case long l: cell.Value = l; break;
            case double d: cell.Value = d; break;
            case decimal m: cell.Value = m; break;
            case bool b: cell.Value = b ? "yes" : "no"; break;
            default: cell.Value = v.ToString(); break;
        }
    }

    /// <summary>UTF-8 with BOM so Excel opens Arabic correctly.</summary>
    public static byte[] Csv(List<OrgCol> cols, IEnumerable<Dictionary<string, object?>> rows)
    {
        var cs = cols.Where(c => !c.Key.StartsWith("_")).ToList();
        var sb = new StringBuilder();
        static string Q(object? v) { var s = v is string str ? Tags.Replace(str, "") : v?.ToString() ?? ""; return s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s; }
        sb.AppendLine(string.Join(",", cs.Select(c => Q(c.Label))));
        foreach (var row in rows) sb.AppendLine(string.Join(",", cs.Select(c => Q(row.TryGetValue(c.Key, out var v) ? v : null))));
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    // ---- the standard exports ----
    public static List<OrgCol> DeptCols(OrgAnalysisResult r) => new()
    {
        C("division", r.Strings["D_division"]), C("dept", r.Strings["D_dept"]), C("headcount", r.Strings["D_people"]), C("head", r.Strings["D_head"]),
        C("underPct", r.Strings["D_under"] + " %"), C("directPct", r.Strings["D_direct"] + " %"), C("external", r.Strings["D_external"]),
        C("supsNoTeamN", r.Strings["D_sups"]), C("issueEmployees", r.Strings["D_issues"]), C("verdict", r.Strings["D_cause"])
    };
    public static List<OrgCol> MgrCols(OrgAnalysisResult r) => new()
    {
        C("code", "Code"), C("name", r.Strings["M_name"]), C("job", r.Strings["M_job"]), C("dept", r.Strings["M_dept"]), C("reports", r.Strings["M_reports"]),
        C("depts", r.Strings["M_depts"]), C("sites", r.Strings["M_sites"]), C("subSups", r.Strings["M_subSups"]), C("topDepts", r.Strings["M_from"]), C("why", r.Strings["M_cause"])
    };
    private static OrgCol C(string k, string l) => new() { Key = k, Label = l };
    public static IEnumerable<Dictionary<string, object?>> DeptRows(OrgAnalysisResult r) => r.Depts.Select(d => new Dictionary<string, object?>
    {
        ["division"] = d.Division, ["dept"] = d.Dept, ["headcount"] = d.Headcount, ["head"] = d.Head, ["underPct"] = d.UnderPct < 0 ? null : d.UnderPct, ["directPct"] = d.DirectPct < 0 ? null : d.DirectPct,
        ["external"] = d.External, ["supsNoTeamN"] = d.SupsNoTeamN, ["issueEmployees"] = d.IssueEmployees, ["verdict"] = d.Verdict
    });
    public static IEnumerable<Dictionary<string, object?>> MgrRows(OrgAnalysisResult r) => r.Managers.Select(m => new Dictionary<string, object?>
    {
        ["code"] = m.Code, ["name"] = m.Name, ["job"] = m.Job, ["dept"] = m.Dept, ["reports"] = m.Reports, ["depts"] = m.Depts, ["sites"] = m.Sites, ["subSups"] = m.SubSups, ["topDepts"] = m.TopDepts, ["why"] = m.Why
    });

    public static byte[] All(OrgAnalysisResult r, string summaryTitle, bool rtl)
    {
        var sheets = new List<Sheet>
        {
            new(summaryTitle, new List<OrgCol> { C("t", "Finding"), C("n", "Count"), C("a", "Accepted"), C("why", "Cause / fix") },
                r.AllFindings.Select(f => new Dictionary<string, object?> { ["t"] = f.Title, ["n"] = f.Pending != null ? null : f.Count, ["a"] = f.Accepted, ["why"] = f.Pending ?? f.Cause })),
            new(r.Strings["D_dept"], DeptCols(r), DeptRows(r)),
            new(r.Strings["M_name"], MgrCols(r), MgrRows(r))
        };
        sheets.AddRange(r.AllFindings.Where(f => f.Rows.Count > 0).Select(f => new Sheet(f.Id, f.Columns, f.Rows)));
        sheets.AddRange(r.Changes.Lists.Where(f => f.Rows.Count > 0).Select(f => new Sheet(f.Id, f.Columns, f.Rows)));
        return Workbook(sheets, rtl);
    }
    public static byte[] Changes(OrgAnalysisResult r, bool rtl) => Workbook(r.Changes.Lists.Select(f => new Sheet(f.Title, f.Columns, f.Rows)), rtl);
    public static byte[] FixList(OrgAnalysisResult r, OrgDeptScore d, bool rtl) => Workbook(new[]
    {
        new Sheet(r.Strings["D_sups"], r.Finding("titleNoTeam")!.Columns, d.SupsNoTeam),
        new Sheet(r.Strings["D_external"], r.Finding("crossLine")!.Columns.Where(c => c.Key is "code" or "name" or "job" or "site" or "mgrName" or "mgrDept").ToList(), d.ExternalRows),
        new Sheet(r.Strings["K_NotUnder"], r.Finding("notUnder")!.Columns, d.NotUnderRows),
        new Sheet(r.Strings["TeamsOver15"], new List<OrgCol> { C("name", r.Strings["M_name"]), C("reports", r.Strings["M_reports"]) }, d.BigSpans.Select(b => new Dictionary<string, object?> { ["name"] = b[0], ["reports"] = b[1] }))
    }, rtl);
}
