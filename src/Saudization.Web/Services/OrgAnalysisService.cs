using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Saudization.Web.Data;
using Saudization.Web.Models;

namespace Saudization.Web.Services;

// ============================================================================================
//  Result model (serialised to the page as JSON and used by the Excel exporter)
// ============================================================================================
public class OrgCol { public string Key { get; set; } = ""; public string Label { get; set; } = ""; public bool Num { get; set; } }

/// <summary>One check: a title, the likely cause / fix, and its rows. Rows are dictionaries so the table, the
/// Excel sheet and the accept list stay generic. "_key" identifies a row for acceptance, "_accepted" marks it.</summary>
public class OrgFinding
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Cause { get; set; } = "";
    public string Tone { get; set; } = "grey";          // danger | warn | grey | ok
    public string? SortKey { get; set; }
    public string? Pending { get; set; }                // set when the check needs data the import doesn't have yet
    public bool CanAccept { get; set; } = true;
    public List<OrgCol> Columns { get; set; } = new();
    public List<Dictionary<string, object?>> Rows { get; set; } = new();
    public int Count => Rows.Count(r => !IsAccepted(r));
    public int Accepted => Rows.Count - Count;
    public static bool IsAccepted(Dictionary<string, object?> r) => r.TryGetValue("_accepted", out var a) && a is true;
}
public class OrgFindingGroup { public string Id { get; set; } = ""; public string Title { get; set; } = ""; public List<OrgFinding> Findings { get; set; } = new(); }

public class OrgDeptScore
{
    public string Key { get; set; } = "";               // "division|department"
    public string Division { get; set; } = ""; public string Dept { get; set; } = "";
    public int Headcount { get; set; }
    public string? Head { get; set; } public string? HeadCode { get; set; } public bool HeadInChart { get; set; } public string? HeadDeptInChart { get; set; }
    public int? UnderHead { get; set; } public int? DirectToHead { get; set; }
    public int UnderPct { get; set; } = -1; public int DirectPct { get; set; } = -1;
    public int External { get; set; }
    public List<object[]> ExternalDepts { get; set; } = new();
    public int Supervisors { get; set; }
    public List<Dictionary<string, object?>> SupsNoTeam { get; set; } = new();
    public int SupsNoTeamN => SupsNoTeam.Count;
    public List<object[]> BigSpans { get; set; } = new();
    public List<object[]> InternalMgrs { get; set; } = new();
    public int IssueEmployees { get; set; }
    public List<object[]> Sites { get; set; } = new();
    public List<Dictionary<string, object?>> ExternalRows { get; set; } = new();
    public List<Dictionary<string, object?>> NotUnderRows { get; set; } = new();
    public string Verdict { get; set; } = ""; public string VerdictCls { get; set; } = "ok";
}
public class OrgMgrScore
{
    public string Code { get; set; } = ""; public string Name { get; set; } = ""; public string Job { get; set; } = ""; public string Dept { get; set; } = "";
    public int Reports { get; set; } public int Depts { get; set; } public int Sites { get; set; } public int Depth { get; set; } public int SubSups { get; set; }
    public string TopDepts { get; set; } = ""; public string Why { get; set; } = ""; public string WhyCls { get; set; } = "ok";
}
public class OrgAnalysisSummary
{
    public int Employees { get; set; } public int Departments { get; set; } public int Divisions { get; set; } public int Managers { get; set; }
    public double AvgSpan { get; set; } public int Over15 { get; set; } public int Over30 { get; set; }
    public List<int[]> DepthHist { get; set; } = new(); public List<object[]> SpanHist { get; set; } = new();
    public int Cross { get; set; } public int NotUnderHead { get; set; } public int HodIssues { get; set; } public int IssuePeople { get; set; } public int Shallow { get; set; }
    public int ChecksWithFindings { get; set; }
}
public class OrgChangeSummary
{
    public int? PreviousVersionId { get; set; } public string? PreviousLabel { get; set; }
    public int Joined { get; set; } public int Left { get; set; } public int Mgr { get; set; } public int Dept { get; set; } public int Job { get; set; }
    public List<object[]> MgrByDept { get; set; } = new();
    public List<OrgFinding> Lists { get; set; } = new();
}
public class OrgAnalysisResult
{
    public int VersionId { get; set; } public string VersionLabel { get; set; } = "";
    public List<object[]> Versions { get; set; } = new();
    public string? Branch { get; set; } public string? Site { get; set; } public string? Division { get; set; }
    public List<string> Branches { get; set; } = new(); public List<string> SitesList { get; set; } = new(); public List<string> DivisionsList { get; set; } = new();
    public OrgAnalysisSummary Summary { get; set; } = new();
    public List<OrgDeptScore> Depts { get; set; } = new();
    public List<OrgMgrScore> Managers { get; set; } = new();
    public List<OrgFindingGroup> Groups { get; set; } = new();
    public OrgChangeSummary Changes { get; set; } = new();
    public Dictionary<string, string> Strings { get; set; } = new();
    public IEnumerable<OrgFinding> AllFindings => Groups.SelectMany(g => g.Findings);
    public OrgFinding? Finding(string id) => AllFindings.Concat(Changes.Lists).FirstOrDefault(f => f.Id == id);
}

// ============================================================================================
//  Engine
// ============================================================================================
/// <summary>Computes the HR hierarchy analysis for one org version: department and manager scorecards, every
/// integrity check with its likely cause, the HOD reference comparison and the diff against the previous
/// version. Texts come from the resource file through <c>T</c>, so the same result feeds the page and Excel.</summary>
public class OrgAnalysisService
{
    private readonly AppDbContext _db;
    public OrgAnalysisService(AppDbContext db) => _db = db;

    private sealed class E
    {
        public string Code = "", Name = "", Branch = "", Site = "", Nat = "", Mgr = "", MgrName = "", Grade = "", Dept = "", Div = "", Job = "";
        public int? GradeNo; public int Depth; public List<E> Reports = new(); public string Level = "";
        public string DeptKey => Dk(Div, Dept);
    }
    private static readonly Regex Ws = new(@"\s+", RegexOptions.Compiled);
    private static string Nk(string? s) => TextNormalizer.Key(s);
    private static string Dk(string div, string dept) => Nk(div) + "|" + Nk(dept);
    private static string Ck(string? s) { var d = new string((s ?? "").Where(char.IsDigit).ToArray()).TrimStart('0'); return d.Length > 0 ? d : (s ?? "").Trim(); }

    // position level from the job title (same rules the chart uses)
    private static readonly Regex RxMgmt = new(@"manager|director|head|chief|\bgm\b|president|owner", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxSup = new(@"supervisor|supervisour|foreman|incharge|in charge|coordinator|team lead|\blead\b|captain", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxProf = new(@"officer|executive|specialist|engineer|accountant|analyst|technician|admin|clerk|nurse|designer|auditor", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxLabor = new(@"laborer|labourer|driver|helper|cleaner|packer|loader|guard|worker|operator|checker|picker|waiter|mechanic", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxTop = new(@"chief|\bceo\b|president|owner|general manager", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxGrade = new(@"\d+", RegexOptions.Compiled);
    private static string LevelOf(string job) =>
        string.IsNullOrWhiteSpace(job) ? "Other" : RxMgmt.IsMatch(job) ? "Management" : RxSup.IsMatch(job) ? "Supervisory" : RxProf.IsMatch(job) ? "Professional" : RxLabor.IsMatch(job) ? "Labor" : "Other";
    private static int Rank(string level) => level switch { "Management" => 4, "Supervisory" => 3, "Professional" => 2, "Labor" => 1, _ => 0 };
    private static int Pct(int a, int b) => b == 0 ? 0 : (int)Math.Round(100.0 * a / b);

    /// <summary>UI strings the page script needs (resource keys OrgA_UI_*).</summary>
    public static readonly string[] UiKeys =
    {
        "Filter", "FilterDepts", "FilterMgrs", "Excel", "Csv", "FixList", "Accept", "Undo", "AcceptedN", "RowsOf", "NoneFound", "OpenInChart",
        "LikelyCause", "Head", "SitsIn", "UnderOf", "DirectOf", "ManagersInside", "IntoOther", "SupsNoTeam", "TeamsOver15", "More", "None", "People", "SitesLbl",
        "NotInChart", "NoHead", "NoHodEntry", "NotFoundVersion", "Of", "Top", "Level", "Levels", "AvailableWhen", "Pending", "AcceptedHint", "AcceptFailed", "ChecksWith", "Affected",
        "K_Employees", "K_EmployeesSub", "K_Managers", "K_ManagersSub", "K_Shallow", "K_ShallowSub", "K_Cross", "K_CrossSub", "K_NotUnder", "K_NotUnderSub", "K_Hod", "K_HodSub", "K_Issue", "K_IssueSub",
        "DepthTitle", "SpanTitle", "DepthNote", "SpanNote",
        "D_division", "D_dept", "D_people", "D_head", "D_under", "D_direct", "D_external", "D_sups", "D_issues", "D_cause", "D_count",
        "M_name", "M_job", "M_dept", "M_reports", "M_depts", "M_sites", "M_subSups", "M_from", "M_cause", "M_count",
        "C_joined", "C_left", "C_mgr", "C_dept", "C_job", "C_note", "C_none"
    };

    public async Task<OrgAnalysisResult> BuildAsync(int versionId, string? branch, string? site, string? division, Func<string, string> T, CancellationToken ct = default)
    {
        string F(string key, params object[] a) => string.Format(T(key), a);

        // ---------- load ----------
        var versions = await _db.OrgVersions.AsNoTracking().OrderByDescending(v => v.Id).Select(v => new { v.Id, v.Label }).ToListAsync(ct);
        var res = new OrgAnalysisResult { VersionId = versionId, VersionLabel = versions.FirstOrDefault(v => v.Id == versionId)?.Label ?? "", Branch = branch, Site = site, Division = division };
        res.Versions = versions.Select(v => new object[] { v.Id, v.Label }).ToList();
        foreach (var k in UiKeys) res.Strings[k] = T("OrgA_UI_" + k);

        var all = await LoadAsync(versionId, ct);
        res.Branches = all.Select(e => e.Branch).Where(s => s.Length > 0).Distinct().OrderBy(s => s).ToList();
        res.SitesList = all.Select(e => e.Site).Where(s => s.Length > 0).Distinct().OrderBy(s => s).ToList();
        res.DivisionsList = all.Select(e => e.Div).Where(s => s.Length > 0).Distinct().OrderBy(s => s).ToList();
        bool InScope(E e) => (string.IsNullOrEmpty(branch) || e.Branch == branch) && (string.IsNullOrEmpty(site) || e.Site == site) && (string.IsNullOrEmpty(division) || e.Div == division);
        var cur = all.Where(InScope).ToList();
        var by = new Dictionary<string, E>();
        foreach (var e in cur) if (e.Code.Length > 0 && !by.ContainsKey(e.Code)) by[e.Code] = e;
        var allBy = new Dictionary<string, E>();
        foreach (var e in all) if (e.Code.Length > 0 && !allBy.ContainsKey(e.Code)) allBy[e.Code] = e;

        foreach (var e in cur) if (e.Mgr.Length > 0 && e.Mgr != e.Code && by.TryGetValue(e.Mgr, out var m)) m.Reports.Add(e);
        List<E> Chain(E e) { var seen = new HashSet<string>(); var o = new List<E>(); var c = e.Mgr; while (c.Length > 0 && c != e.Code && by.TryGetValue(c, out var m) && seen.Add(c)) { o.Add(m); c = m.Mgr; } return o; }
        foreach (var e in cur) { e.Depth = Chain(e).Count; e.Level = LevelOf(e.Job); }
        var reports = cur.Where(e => e.Reports.Count > 0).ToList();

        var heads = await LoadHeadsAsync(versionId, ct);
        var hodBy = new Dictionary<string, DepartmentHead>();
        foreach (var h in heads) hodBy.TryAdd(Dk(h.Division, h.Department), h);
        var prevId = versions.Where(v => v.Id < versionId).Select(v => (int?)v.Id).FirstOrDefault();
        var prev = prevId is int pid ? (await LoadAsync(pid, ct)).Where(InScope).ToList() : new List<E>();
        var prevBy = new Dictionary<string, E>(); foreach (var e in prev) if (e.Code.Length > 0) prevBy.TryAdd(e.Code, e);
        var weeklyCodes = new HashSet<string>((await _db.Employees.AsNoTracking()
            .Where(x => x.Snapshot!.IsDefault).Select(x => x.WorkerNo).ToListAsync(ct))
            .Select(w => Ck((w ?? "").Split('-').Last())).Where(s => s.Length > 0));
        var accepts = (await _db.OrgFindingAccepts.AsNoTracking().Select(a => new { a.FindingId, a.RowKey }).ToListAsync(ct))
            .GroupBy(a => a.FindingId).ToDictionary(g => g.Key, g => g.Select(a => a.RowKey).ToHashSet());

        // ---------- helpers ----------
        string Lvl(string l) => T("OrgA_Lvl_" + l);
        Dictionary<string, object?> Row(E e, params (string k, object? v)[] extra)
        {
            var r = new Dictionary<string, object?> { ["_key"] = e.Code, ["code"] = e.Code, ["name"] = e.Name, ["job"] = e.Job, ["dept"] = e.Dept, ["division"] = e.Div, ["site"] = e.Site, ["reports"] = e.Reports.Count };
            foreach (var (k, v) in extra) r[k] = v;
            return r;
        }
        OrgCol Col(string key, bool num = false, string? labelKey = null) => new() { Key = key, Label = T("OrgA_Col_" + (labelKey ?? key)), Num = num };
        List<OrgCol> Cols(params string[] keys) => keys.Select(k => k.EndsWith("#") ? Col(k.TrimEnd('#'), true) : Col(k)).ToList();
        OrgFinding Fd(string id, string tone, List<OrgCol> cols, List<Dictionary<string, object?>> rows, string? sortKey = null, params object[] causeArgs)
        {
            var f = new OrgFinding { Id = id, Title = T("OrgA_F_" + id + "_T"), Cause = causeArgs.Length > 0 ? F("OrgA_F_" + id + "_C", causeArgs) : T("OrgA_F_" + id + "_C"), Tone = tone, SortKey = sortKey, Columns = cols, Rows = rows };
            if (accepts.TryGetValue(id, out var keys)) foreach (var r in rows) if (r["_key"] is string k && keys.Contains(k)) r["_accepted"] = true;
            if (f.Count == 0) f.Tone = "ok";
            return f;
        }

        // ---------- departments ----------
        var deptKeys = cur.Select(e => (e.Div, e.Dept)).Distinct().OrderBy(x => x.Div).ThenBy(x => x.Dept).ToList();
        var notUnderRows = new List<Dictionary<string, object?>>(); var supsNoTeamAll = new List<Dictionary<string, object?>>();
        foreach (var (div, dept) in deptKeys)
        {
            var lst = cur.Where(e => e.Div == div && e.Dept == dept).ToList();
            hodBy.TryGetValue(Dk(div, dept), out var h);
            var headCode = h?.HeadCode?.Trim(); E? head = headCode is { Length: > 0 } && by.TryGetValue(headCode, out var hh) ? hh : null;
            var d = new OrgDeptScore { Key = div + "|" + dept, Division = div, Dept = dept, Headcount = lst.Count, Head = h?.HeadName, HeadCode = headCode, HeadInChart = head != null, HeadDeptInChart = head?.Dept };
            if (head != null)
            {
                int under = 0, direct = 0;
                foreach (var e in lst)
                {
                    if (e == head) { under++; continue; }
                    var chainOf = Chain(e);
                    if (chainOf.Contains(head)) under++;
                    else { var r = Row(e, ("head", h!.HeadName), ("mgrName", by.TryGetValue(e.Mgr, out var mm) ? mm.Name : e.Mgr), ("mgrDept", by.TryGetValue(e.Mgr, out var m2) ? m2.Dept : "")); notUnderRows.Add(r); d.NotUnderRows.Add(r); }
                    if (e.Mgr == head.Code) direct++;
                }
                d.UnderHead = under; d.DirectToHead = direct; d.UnderPct = Pct(under, lst.Count); d.DirectPct = Pct(direct, lst.Count);
            }
            var ext = lst.Where(e => e.Mgr != e.Code && by.TryGetValue(e.Mgr, out var m) && Nk(m.Dept) != Nk(dept)).Select(e => (e, m: by[e.Mgr])).ToList();
            d.External = ext.Count;
            d.ExternalDepts = ext.GroupBy(x => x.m.Dept).OrderByDescending(g => g.Count()).Take(5).Select(g => new object[] { g.Key, g.Count() }).ToList();
            d.ExternalRows = ext.Select(x => Row(x.e, ("mgrName", x.m.Name), ("mgrDept", x.m.Dept))).ToList();
            var sups = lst.Where(e => (e.Level == "Supervisory" || e.Level == "Management") && e != head).ToList();
            d.Supervisors = sups.Count;
            d.SupsNoTeam = sups.Where(e => e.Reports.Count == 0).Select(e => Row(e, ("level", Lvl(e.Level)))).ToList();
            supsNoTeamAll.AddRange(d.SupsNoTeam);
            d.BigSpans = lst.Where(e => e.Reports.Count > 15).OrderByDescending(e => e.Reports.Count).Select(e => new object[] { e.Name, e.Reports.Count }).ToList();
            d.InternalMgrs = lst.Where(e => e.Mgr != e.Code && by.TryGetValue(e.Mgr, out var m) && Nk(m.Dept) == Nk(dept) && m != head).GroupBy(e => by[e.Mgr].Name)
                .OrderByDescending(g => g.Count()).Take(5).Select(g => new object[] { g.Key, g.Count() }).ToList();
            d.Sites = lst.GroupBy(e => e.Site).OrderByDescending(g => g.Count()).Take(3).Select(g => new object[] { g.Key, g.Count() }).ToList();
            var issues = new HashSet<string>(ext.Select(x => x.e.Code));
            issues.UnionWith(d.SupsNoTeam.Select(r => (string)r["code"]!));
            issues.UnionWith(lst.Where(e => e.Mgr.Length == 0 || e.Mgr == e.Code || !by.ContainsKey(e.Mgr)).Select(e => e.Code));
            issues.UnionWith(d.NotUnderRows.Select(r => (string)r["code"]!));
            d.IssueEmployees = issues.Count;
            // verdict
            if (h != null && head == null) (d.VerdictCls, d.Verdict) = ("bad", F("OrgA_V_headGone", h.HeadName ?? headCode ?? ""));
            else if (h == null) (d.VerdictCls, d.Verdict) = ("mid", T("OrgA_V_noHead"));
            else if (Pct(d.External, lst.Count) >= 50) (d.VerdictCls, d.Verdict) = ("bad", F("OrgA_V_external", Pct(d.External, lst.Count), string.Join(", ", d.ExternalDepts.Take(2).Select(x => x[0]))));
            else if (d.DirectPct >= 50 && d.SupsNoTeam.Count > 0) (d.VerdictCls, d.Verdict) = ("bad", F("OrgA_V_direct", d.DirectPct, d.SupsNoTeam.Count));
            else if (d.UnderPct < 60) (d.VerdictCls, d.Verdict) = ("mid", F("OrgA_V_under", d.UnderPct));
            else if (head != null && Nk(head.Dept) != Nk(dept)) (d.VerdictCls, d.Verdict) = ("mid", F("OrgA_V_headElsewhere", head.Dept));
            else if (d.SupsNoTeam.Count > 0) (d.VerdictCls, d.Verdict) = ("mid", F("OrgA_V_supsNoTeam", d.SupsNoTeam.Count));
            else (d.VerdictCls, d.Verdict) = ("ok", T("OrgA_V_ok"));
            res.Depts.Add(d);
        }
        res.Depts = res.Depts.OrderByDescending(d => d.IssueEmployees).ThenByDescending(d => d.Headcount).ToList();

        // ---------- managers ----------
        foreach (var m in reports.OrderByDescending(m => m.Reports.Count))
        {
            var ds = m.Reports.GroupBy(r => r.Dept).OrderByDescending(g => g.Count()).ToList();
            var ms = new OrgMgrScore { Code = m.Code, Name = m.Name, Job = m.Job, Dept = m.Dept, Reports = m.Reports.Count, Depts = ds.Count, Sites = m.Reports.Select(r => r.Site).Distinct().Count(), Depth = m.Depth,
                SubSups = m.Reports.Count(r => r.Level is "Supervisory" or "Management"), TopDepts = string.Join(" · ", ds.Take(3).Select(g => g.Key + " " + g.Count())) };
            if (ms.Reports > 30 && ms.Depts >= 5) (ms.WhyCls, ms.Why) = ("bad", T("OrgA_M_headForDepts"));
            else if (ms.Reports > 15 && ms.SubSups >= 2) (ms.WhyCls, ms.Why) = ("mid", F("OrgA_M_subSups", ms.SubSups));
            else if (ms.Reports > 15) (ms.WhyCls, ms.Why) = ("mid", T("OrgA_M_noLayer"));
            res.Managers.Add(ms);
        }

        // ---------- findings: reporting lines ----------
        var over15 = reports.Where(m => m.Reports.Count > 15).OrderByDescending(m => m.Reports.Count)
            .Select(m => Row(m, ("depts", m.Reports.Select(r => r.Dept).Distinct().Count()), ("sites", m.Reports.Select(r => r.Site).Distinct().Count()), ("subSups", m.Reports.Count(r => r.Level is "Supervisory" or "Management")))).ToList();
        var cross = new List<Dictionary<string, object?>>();
        foreach (var e in cur)
        {
            if (e.Mgr == e.Code || !by.TryGetValue(e.Mgr, out var m)) continue;
            bool dd = Nk(m.Dept) != Nk(e.Dept), dv = Nk(m.Div) != Nk(e.Div), ds = e.Site.Length > 0 && m.Site.Length > 0 && Nk(m.Site) != Nk(e.Site);
            if (dd || dv || ds) cross.Add(Row(e, ("mgrName", m.Name), ("mgrDept", m.Dept), ("mgrDivision", m.Div), ("mgrSite", m.Site), ("severity", T("OrgA_Sev_" + (ds ? "site" : dv ? "division" : "department"))), ("_sev", ds ? 3 : dv ? 2 : 1)));
        }
        var around = cur.Where(e => e.Mgr != e.Code && by.TryGetValue(e.Mgr, out var m) && Nk(m.Dept) != Nk(e.Dept) && by.TryGetValue(m.Mgr, out var mm) && mm != m && Nk(mm.Dept) == Nk(e.Dept))
            .Select(e => Row(e, ("mgrName", by[e.Mgr].Name), ("mgrDept", by[e.Mgr].Dept), ("mgrMgr", by[by[e.Mgr].Mgr].Name))).ToList();
        var tops = cur.Where(e => e.Mgr.Length == 0 || e.Mgr == e.Code).OrderByDescending(e => e.Reports.Count).Select(e =>
        {
            bool noMgr = e.Mgr.Length == 0;
            string why = noMgr && e.Reports.Count == 0 ? F("OrgA_Top_notTop", e.Job) : RxTop.IsMatch(e.Job) ? T("OrgA_Top_real") : e.Reports.Count >= 20 ? T("OrgA_Top_big") : F("OrgA_Top_small", e.Reports.Count);
            return Row(e, ("case", T(noMgr ? "OrgA_Case_noMgr" : "OrgA_Case_ownMgr")), ("why", why));
        }).ToList();
        var leftCodes = prevBy.Keys.Except(by.Keys).ToHashSet();
        var dangling = cur.Where(e => e.Mgr.Length > 0 && e.Mgr != e.Code && !by.ContainsKey(e.Mgr)).Select(e =>
        {
            string cls, hint = "";
            if (allBy.ContainsKey(e.Mgr)) { cls = T("OrgA_Dang_scope"); hint = allBy[e.Mgr].Name; }
            else if (prevBy.TryGetValue(e.Mgr, out var p)) { cls = T("OrgA_Dang_left"); hint = p.Name; }
            else if (weeklyCodes.Contains(Ck(e.Mgr))) cls = T("OrgA_Dang_payroll");
            else cls = T("OrgA_Dang_typo");
            return Row(e, ("mgrCode", e.Mgr), ("classification", cls), ("hint", hint));
        }).ToList();
        var leaverMgr = cur.Where(e => leftCodes.Contains(e.Mgr)).Select(e => Row(e, ("mgrCode", e.Mgr), ("mgrName", prevBy[e.Mgr].Name))).ToList();
        var cycles = new List<Dictionary<string, object?>>();
        foreach (var e in cur)
        {
            var seen = new HashSet<string>(); var c = e; int hops = 0;
            while (c.Mgr.Length > 0 && by.TryGetValue(c.Mgr, out var n) && hops++ < 5000)
            {
                if (n == e && c != e) { cycles.Add(Row(e, ("mgrName", by.TryGetValue(e.Mgr, out var m0) ? m0.Name : ""))); break; }
                if (!seen.Add(n.Code)) break; c = n;
            }
        }
        var inversion = new List<Dictionary<string, object?>>();
        foreach (var e in cur)
        {
            if (e.Mgr == e.Code || !by.TryGetValue(e.Mgr, out var m)) continue;
            bool inv = e.GradeNo is int g && m.GradeNo is int mg ? g > mg : Rank(e.Level) > Rank(m.Level) && Rank(m.Level) > 0;
            if (inv) inversion.Add(Row(e, ("level", Lvl(e.Level)), ("grade", e.Grade), ("mgrName", m.Name), ("mgrJob", m.Job), ("mgrLevel", Lvl(m.Level)), ("mgrGrade", m.Grade)));
        }
        var junior = reports.Where(m => m.Level == "Labor").OrderByDescending(m => m.Reports.Count).Select(m => Row(m)).ToList();
        var titleNoTeam = cur.Where(e => e.Level is "Management" or "Supervisory" && e.Reports.Count == 0).Select(e => Row(e, ("level", Lvl(e.Level)))).ToList();
        var single = reports.Where(m => m.Reports.Count == 1).Select(m => Row(m, ("onlyReport", m.Reports[0].Name))).ToList();

        var lines = new OrgFindingGroup { Id = "lines", Title = T("OrgA_G_lines") };
        lines.Findings.Add(Fd("headAsManager", "danger", Cols("code", "name", "job", "dept", "reports#", "depts#", "sites#", "subSups#"), over15, "reports", over15.Count));
        lines.Findings.Add(Fd("crossLine", "warn", Cols("code", "name", "job", "dept", "site", "mgrName", "mgrDept", "mgrSite", "severity"), cross, "_sev"));
        lines.Findings.Add(Fd("notUnder", "warn", Cols("code", "name", "job", "dept", "head", "mgrName", "mgrDept"), notUnderRows));
        lines.Findings.Add(Fd("around", "warn", Cols("code", "name", "job", "dept", "mgrName", "mgrDept", "mgrMgr"), around));
        lines.Findings.Add(Fd("tops", "warn", Cols("code", "name", "job", "dept", "site", "case", "reports#", "why"), tops, "reports", Math.Max(0, tops.Count - 1)));
        lines.Findings.Add(Fd("dangling", "danger", Cols("code", "name", "job", "dept", "mgrCode", "classification", "hint"), dangling));
        lines.Findings.Add(Fd("leaverMgr", "danger", Cols("code", "name", "job", "dept", "mgrCode", "mgrNameLeft"), leaverMgr));
        lines.Findings.Add(Fd("cycles", "danger", Cols("code", "name", "job", "dept", "mgrName"), cycles));
        lines.Findings.Add(Fd("inversion", "warn", Cols("code", "name", "job", "level", "grade", "dept", "mgrName", "mgrJob", "mgrLevel", "mgrGrade"), inversion));
        lines.Findings.Add(Fd("junior", "warn", Cols("code", "name", "job", "dept", "site", "reports#"), junior, "reports"));
        lines.Findings.Add(Fd("titleNoTeam", "grey", Cols("code", "name", "job", "level", "dept", "division", "site"), titleNoTeam));
        lines.Findings.Add(Fd("single", "grey", Cols("code", "name", "job", "dept", "onlyReport"), single));
        res.Groups.Add(lines);

        // ---------- findings: department heads ----------
        var chartK = deptKeys.Select(x => Dk(x.Div, x.Dept)).ToHashSet();
        string HK(DepartmentHead h) => h.Division + "|" + h.Department;
        var hodMissing = heads.Where(h => !chartK.Contains(Dk(h.Division, h.Department))).Select(h => new Dictionary<string, object?> { ["_key"] = HK(h), ["division"] = h.Division, ["dept"] = h.Department, ["head"] = h.HeadName }).ToList();
        var hodNoHead = deptKeys.Where(x => !hodBy.ContainsKey(Dk(x.Div, x.Dept))).Select(x => new Dictionary<string, object?> { ["_key"] = x.Div + "|" + x.Dept, ["division"] = x.Div, ["dept"] = x.Dept, ["people"] = cur.Count(e => e.Div == x.Div && e.Dept == x.Dept) }).ToList();
        var hodGone = heads.Where(h => h.HeadCode is { Length: > 0 } && !by.ContainsKey(h.HeadCode.Trim())).Select(h => new Dictionary<string, object?> { ["_key"] = h.HeadCode!.Trim() + "|" + HK(h), ["code"] = h.HeadCode, ["head"] = h.HeadName, ["dept"] = h.Department,
            ["classification"] = T(allBy.ContainsKey(h.HeadCode!.Trim()) ? "OrgA_Dang_scope" : prevBy.ContainsKey(h.HeadCode!.Trim()) ? "OrgA_Hod_left" : "OrgA_Hod_unknown") }).ToList();
        var hodName = heads.Where(h => h.HeadCode is { Length: > 0 } && by.TryGetValue(h.HeadCode.Trim(), out var e) && Nk(h.HeadName) != Nk(e.Name)).Select(h => new Dictionary<string, object?> { ["_key"] = h.HeadCode!.Trim() + "|" + HK(h), ["code"] = h.HeadCode, ["hodName"] = h.HeadName, ["chartName"] = by[h.HeadCode!.Trim()].Name, ["dept"] = h.Department }).ToList();
        var hodOther = heads.Where(h => h.HeadCode is { Length: > 0 } && by.TryGetValue(h.HeadCode.Trim(), out var e) && Nk(e.Dept) != Nk(h.Department)).Select(h => { var e = by[h.HeadCode!.Trim()]; return new Dictionary<string, object?> { ["_key"] = h.HeadCode!.Trim() + "|" + HK(h), ["code"] = h.HeadCode, ["head"] = h.HeadName, ["heads"] = h.Department, ["sitsIn"] = e.Dept, ["sitsInDivision"] = e.Div }; }).ToList();
        var hodInv = heads.Where(h => h.HeadCode is { Length: > 0 } && by.TryGetValue(h.HeadCode.Trim(), out var e) && e.Mgr != e.Code && by.TryGetValue(e.Mgr, out var m) && Nk(m.Dept) == Nk(h.Department))
            .Select(h => { var e = by[h.HeadCode!.Trim()]; var m = by[e.Mgr]; return new Dictionary<string, object?> { ["_key"] = h.HeadCode!.Trim() + "|" + HK(h), ["code"] = h.HeadCode, ["head"] = h.HeadName, ["dept"] = h.Department, ["reportsTo"] = m.Name, ["reportsToJob"] = m.Job }; }).ToList();
        static List<Dictionary<string, object?>> Dedupe(List<Dictionary<string, object?>> rows) => rows.GroupBy(r => (string)r["_key"]!).Select(g => g.First()).ToList();
        var hodG = new OrgFindingGroup { Id = "heads", Title = T("OrgA_G_heads") };
        hodG.Findings.Add(Fd("hodMissing", "warn", Cols("division", "dept", "head"), Dedupe(hodMissing)));
        hodG.Findings.Add(Fd("hodNoHead", "warn", Cols("division", "dept", "people#"), hodNoHead, "people"));
        hodG.Findings.Add(Fd("hodGone", "danger", Cols("code", "head", "dept", "classification"), Dedupe(hodGone)));
        hodG.Findings.Add(Fd("hodOtherDept", "warn", Cols("code", "head", "heads", "sitsIn", "sitsInDivision"), Dedupe(hodOther)));
        hodG.Findings.Add(Fd("hodName", "grey", Cols("code", "hodName", "chartName", "dept"), Dedupe(hodName)));
        hodG.Findings.Add(Fd("hodInverted", "danger", Cols("code", "head", "dept", "reportsTo", "reportsToJob"), Dedupe(hodInv)));
        res.Groups.Add(hodG);

        // ---------- findings: data quality ----------
        var dups = cur.GroupBy(e => Nk(e.Name)).Where(g => g.Key.Length > 0 && g.Count() > 1)
            .SelectMany(g => g.Select(e => Row(e, ("otherCodes", string.Join(", ", g.Where(x => x != e).Select(x => x.Code)))))).ToList();
        var generic = deptKeys.GroupBy(x => Nk(x.Dept)).Where(g => g.Select(x => x.Div).Distinct().Count() > 1)
            .Select(g => new Dictionary<string, object?> { ["_key"] = g.Key, ["dept"] = g.First().Dept, ["divisions"] = string.Join(", ", g.Select(x => x.Div).Distinct().OrderBy(x => x)), ["count"] = g.Select(x => x.Div).Distinct().Count() }).ToList();
        bool hasSite = all.Any(e => e.Site.Length > 0);   // an export without a Site column must not flag everyone
        var missingSite = hasSite ? cur.Where(e => e.Site.Length == 0).Select(e => Row(e)).ToList() : new List<Dictionary<string, object?>>();
        bool hasMgrNames = cur.Any(e => e.MgrName.Length > 0);
        var mgrNameMismatch = !hasMgrNames ? new List<Dictionary<string, object?>>() : cur.Where(e => e.MgrName.Length > 0 && e.Mgr != e.Code && by.TryGetValue(e.Mgr, out var m) && !SameName(m.Name, e.MgrName))
            .Select(e => Row(e, ("mgrCode", e.Mgr), ("mgrName", by[e.Mgr].Name), ("mgrNameFile", e.MgrName))).ToList();
        var qual = new OrgFindingGroup { Id = "quality", Title = T("OrgA_G_quality") };
        qual.Findings.Add(Fd("dups", "danger", Cols("code", "name", "job", "dept", "site", "reports#", "otherCodes"), dups));
        qual.Findings.Add(Fd("generic", "grey", Cols("dept", "divisions", "count#"), generic, "count"));
        var siteCheck = Fd("missingSite", "grey", Cols("code", "name", "job", "dept", "division"), missingSite);
        if (!hasSite) { siteCheck.Pending = T("OrgA_F_missingSite_P"); siteCheck.Tone = "grey"; }
        qual.Findings.Add(siteCheck);
        var mn = Fd("mgrNameMismatch", "danger", Cols("code", "name", "dept", "mgrCode", "mgrName", "mgrNameFile"), mgrNameMismatch);
        if (!hasMgrNames) { mn.Pending = T("OrgA_F_mgrNameMismatch_P"); mn.Tone = "grey"; }
        qual.Findings.Add(mn);
        qual.Findings.Add(new OrgFinding { Id = "payroll", Title = T("OrgA_F_payroll_T"), Cause = T("OrgA_F_payroll_C"), Pending = T("OrgA_F_payroll_P"), Tone = "grey", CanAccept = false, Columns = Cols("code", "name") });
        res.Groups.Add(qual);

        // ---------- changes vs previous version ----------
        var ch = res.Changes; ch.PreviousVersionId = prevId; ch.PreviousLabel = versions.FirstOrDefault(v => v.Id == prevId)?.Label;
        if (prevId != null)
        {
            var pk = new Dictionary<string, E>(); foreach (var e in prev) pk.TryAdd(Ck(e.Code), e);
            var ckm = new Dictionary<string, E>(); foreach (var e in cur) ckm.TryAdd(Ck(e.Code), e);
            var joined = ckm.Where(kv => !pk.ContainsKey(kv.Key)).Select(kv => Row(kv.Value)).ToList();
            var left = pk.Where(kv => !ckm.ContainsKey(kv.Key)).Select(kv => new Dictionary<string, object?> { ["_key"] = kv.Value.Code, ["code"] = kv.Value.Code, ["name"] = kv.Value.Name, ["job"] = kv.Value.Job, ["dept"] = kv.Value.Dept, ["site"] = kv.Value.Site, ["wasManagerOf"] = prev.Count(x => x.Mgr == kv.Value.Code) }).ToList();
            string MgrLabel(Dictionary<string, E> dict, string code) => dict.TryGetValue(code, out var m) ? m.Name : code;
            var mgrCh = ckm.Where(kv => pk.TryGetValue(kv.Key, out var p) && p.Mgr != kv.Value.Mgr).Select(kv => Row(kv.Value, ("fromMgr", MgrLabel(prevBy, pk[kv.Key].Mgr)), ("toMgr", MgrLabel(by, kv.Value.Mgr)))).ToList();
            var deptCh = ckm.Where(kv => pk.TryGetValue(kv.Key, out var p) && Nk(p.Dept) != Nk(kv.Value.Dept)).Select(kv => Row(kv.Value, ("fromDept", pk[kv.Key].Dept), ("toDept", kv.Value.Dept))).ToList();
            var jobCh = ckm.Where(kv => pk.TryGetValue(kv.Key, out var p) && Nk(p.Job) != Nk(kv.Value.Job)).Select(kv => Row(kv.Value, ("fromJob", pk[kv.Key].Job), ("toJob", kv.Value.Job))).ToList();
            ch.Joined = joined.Count; ch.Left = left.Count; ch.Mgr = mgrCh.Count; ch.Dept = deptCh.Count; ch.Job = jobCh.Count;
            ch.MgrByDept = mgrCh.GroupBy(r => (string)r["dept"]!).OrderByDescending(g => g.Count()).Take(8).Select(g => new object[] { g.Key, g.Count() }).ToList();
            OrgFinding ChF(string id, string tone, List<OrgCol> cols, List<Dictionary<string, object?>> rows, string? sort = null) => new() { Id = id, Title = T("OrgA_F_" + id + "_T"), Cause = T("OrgA_F_" + id + "_C"), Tone = rows.Count == 0 ? "ok" : tone, Columns = cols, Rows = rows, SortKey = sort, CanAccept = false };
            ch.Lists.Add(ChF("chgMgr", "danger", Cols("code", "name", "job", "dept", "fromMgr", "toMgr"), mgrCh));
            ch.Lists.Add(ChF("chgDept", "warn", Cols("code", "name", "job", "fromDept", "toDept"), deptCh));
            ch.Lists.Add(ChF("chgJoined", "ok", Cols("code", "name", "job", "dept", "division", "site"), joined));
            ch.Lists.Add(ChF("chgLeft", "grey", Cols("code", "name", "job", "dept", "site", "wasManagerOf#"), left, "wasManagerOf"));
            ch.Lists.Add(ChF("chgJob", "grey", Cols("code", "name", "dept", "fromJob", "toJob"), jobCh));
        }

        // ---------- summary ----------
        var s = res.Summary;
        s.Employees = cur.Count; s.Departments = deptKeys.Count; s.Divisions = deptKeys.Select(x => x.Div).Distinct().Count(); s.Managers = reports.Count;
        s.AvgSpan = reports.Count == 0 ? 0 : Math.Round(reports.Sum(m => m.Reports.Count) / (double)reports.Count, 1);
        s.Over15 = reports.Count(m => m.Reports.Count > 15); s.Over30 = reports.Count(m => m.Reports.Count > 30);
        s.DepthHist = cur.GroupBy(e => e.Depth).OrderBy(g => g.Key).Select(g => new[] { g.Key, g.Count() }).ToList();
        int Span(int lo, int hi) => reports.Count(m => m.Reports.Count >= lo && m.Reports.Count <= hi);
        s.SpanHist = new List<object[]> { new object[] { "1", Span(1, 1) }, new object[] { "2-5", Span(2, 5) }, new object[] { "6-10", Span(6, 10) }, new object[] { "11-15", Span(11, 15) }, new object[] { "16-30", Span(16, 30) }, new object[] { ">30", Span(31, int.MaxValue) } };
        s.Shallow = cur.Count(e => e.Depth <= 3);
        s.Cross = res.Finding("crossLine")!.Count; s.NotUnderHead = res.Finding("notUnder")!.Count;
        s.HodIssues = hodG.Findings.Sum(f => f.Count);
        var people = new HashSet<string>();
        foreach (var f in res.AllFindings) foreach (var r in f.Rows) if (!OrgFinding.IsAccepted(r) && r.TryGetValue("code", out var c) && c is string cs && by.ContainsKey(cs)) people.Add(cs);
        s.IssuePeople = people.Count;
        s.ChecksWithFindings = res.AllFindings.Count(f => f.Count > 0);
        return res;
    }

    private static bool SameName(string a, string b)
    {
        static HashSet<string> Tok(string s) => Nk(s).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => t is not ("al" or "el" or "bin" or "ibn" or "abdul" or "abdel" or "abd")).ToHashSet();
        var x = Tok(a); var y = Tok(b);
        if (x.Count == 0 || y.Count == 0) return true;
        return x.IsSubsetOf(y) || y.IsSubsetOf(x) || x.Intersect(y).Count() >= Math.Min(2, Math.Min(x.Count, y.Count));
    }

    private async Task<List<E>> LoadAsync(int versionId, CancellationToken ct)
    {
        var divName = await _db.OrgDivisions.AsNoTracking().Where(x => x.OrgVersionId == versionId).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var dept = await _db.OrgDepartments.AsNoTracking().Where(x => x.OrgVersionId == versionId).Select(x => new { x.Id, x.Name, x.OrgDivisionId }).ToDictionaryAsync(x => x.Id, ct);
        var jobName = await _db.OrgJobs.AsNoTracking().Where(x => x.OrgVersionId == versionId).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var emps = await _db.OrgEmployees.AsNoTracking().Where(x => x.OrgVersionId == versionId).ToListAsync(ct);
        return emps.Select(x =>
        {
            dept.TryGetValue(x.OrgDepartmentId, out var d);
            var grade = x.Grade ?? "";
            var gm = RxGrade.Match(grade);
            return new E
            {
                Code = (x.Code ?? "").Trim(), Name = x.Name ?? "", Branch = x.BranchCode ?? "", Site = x.Site ?? "", Nat = x.Nationality ?? "",
                Mgr = NormMgr(x.DirectManagerCode), MgrName = x.DirectManagerName ?? "", Grade = grade, GradeNo = gm.Success ? int.Parse(gm.Value) : null,
                Dept = d?.Name ?? "", Div = d != null && divName.TryGetValue(d.OrgDivisionId, out var dn) ? dn : "", Job = x.OrgJobId is int jid && jobName.TryGetValue(jid, out var jn) ? jn : ""
            };
        }).ToList();
    }

    // "NO_MANAGER" / "NO MANAGER" in the HR export means no manager
    private static string NormMgr(string? code)
    {
        var c = (code ?? "").Trim();
        return c.Replace(" ", "_").Equals("NO_MANAGER", StringComparison.OrdinalIgnoreCase) ? "" : c;
    }

    /// <summary>The HOD rows for a version: the sheet imported with it, else the latest imported sheet, else the legacy global rows.</summary>
    public async Task<List<DepartmentHead>> LoadHeadsAsync(int versionId, CancellationToken ct)
    {
        var own = await _db.DepartmentHeads.AsNoTracking().Where(h => h.OrgVersionId == versionId).ToListAsync(ct);
        if (own.Count > 0) return own;
        var latest = await _db.DepartmentHeads.AsNoTracking().Where(h => h.OrgVersionId != null && h.OrgVersionId < versionId).Select(h => h.OrgVersionId).OrderByDescending(v => v).FirstOrDefaultAsync(ct);
        return await _db.DepartmentHeads.AsNoTracking().Where(h => h.OrgVersionId == latest).ToListAsync(ct);
    }
}
