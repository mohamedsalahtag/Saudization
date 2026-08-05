using Saudization.Web.Models;

namespace Saudization.Web.Services;

/// <summary>
/// Manager-consistency analysis: an employee is consistent when their direct manager sits in the same
/// "directory". Directory = Division + Department (or Division only when <paramref>divisionOnly</paramref>).
/// Mirrored client-side in restructure.js so live edits update the warnings without a round-trip.
/// </summary>
public static class OrgConsistency
{
    public static ConsistencyResult Analyze(OrgModelDto m, bool divisionOnly = false)
    {
        var deptDiv = m.Departments.ToDictionary(d => d.Id, d => d.DivisionId);
        var deptName = m.Departments.ToDictionary(d => d.Id, d => d.Name);
        var divName = m.Divisions.ToDictionary(d => d.Id, d => d.Name);
        var byCode = new Dictionary<string, OrgEmpDto>();
        foreach (var e in m.Employees)
            if (!string.IsNullOrWhiteSpace(e.Code)) byCode[e.Code!.Trim()] = e;

        var res = new ConsistencyResult { Total = m.Employees.Count };
        foreach (var e in m.Employees)
        {
            var mc = e.ManagerCode?.Trim();
            if (string.IsNullOrWhiteSpace(mc) || mc.Equals("NO_MANAGER", StringComparison.OrdinalIgnoreCase))
            { res.NoManager++; continue; }
            if (!byCode.TryGetValue(mc, out var mgr))
            {
                res.Dangling++;
                res.Inconsistent.Add(Row(e, null, deptDiv, deptName, divName, "dangling"));
                continue;
            }

            int eDiv = deptDiv.GetValueOrDefault(e.DepartmentId), mDiv = deptDiv.GetValueOrDefault(mgr.DepartmentId);
            bool sameDiv = eDiv == mDiv, sameDept = e.DepartmentId == mgr.DepartmentId;
            bool ok = divisionOnly ? sameDiv : (sameDiv && sameDept);
            if (ok) { res.Consistent++; continue; }

            var reason = !sameDiv && !sameDept ? "cross-both" : !sameDiv ? "cross-division" : "cross-department";
            res.Inconsistent.Add(Row(e, mgr, deptDiv, deptName, divName, reason));
        }
        return res;
    }

    private static InconsistentRow Row(OrgEmpDto e, OrgEmpDto? mgr,
        Dictionary<int, int> deptDiv, Dictionary<int, string> deptName, Dictionary<int, string> divName, string reason)
    {
        string DivOf(int deptId) => deptDiv.TryGetValue(deptId, out var dv) && divName.TryGetValue(dv, out var n) ? n : "";
        return new InconsistentRow
        {
            EmpCode = e.Code,
            EmpName = e.Name,
            EmpDivision = DivOf(e.DepartmentId),
            EmpDepartment = deptName.GetValueOrDefault(e.DepartmentId, ""),
            ManagerCode = mgr?.Code ?? e.ManagerCode,
            ManagerName = mgr?.Name,
            ManagerDivision = mgr != null ? DivOf(mgr.DepartmentId) : "",
            ManagerDepartment = mgr != null ? deptName.GetValueOrDefault(mgr.DepartmentId, "") : "",
            Reason = reason
        };
    }
}
