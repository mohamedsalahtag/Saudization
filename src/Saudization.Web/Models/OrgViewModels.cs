namespace Saudization.Web.Models;

// ---- serialized to the browser; restructure.js renders the tree/table and recomputes live ----

public class OrgDivisionDto { public int Id { get; set; } public string Name { get; set; } = ""; }
public class OrgDeptDto { public int Id { get; set; } public string Name { get; set; } = ""; public int DivisionId { get; set; } }
public class OrgJobDto { public int Id { get; set; } public string Name { get; set; } = ""; public int DepartmentId { get; set; } }

public class OrgEmpDto
{
    public int Id { get; set; }
    public string? Code { get; set; }
    public string Name { get; set; } = "";
    public string? Site { get; set; }
    public string? ManagerCode { get; set; }
    public int DepartmentId { get; set; }
    public int? JobId { get; set; }
}

public class OrgModelDto
{
    public int VersionId { get; set; }
    public string Label { get; set; } = "";
    public bool IsOriginal { get; set; }
    public List<OrgDivisionDto> Divisions { get; set; } = new();
    public List<OrgDeptDto> Departments { get; set; } = new();
    public List<OrgJobDto> Jobs { get; set; } = new();
    public List<OrgEmpDto> Employees { get; set; } = new();
}

// ---- manager consistency ----

public class InconsistentRow
{
    public string? EmpCode { get; set; }
    public string EmpName { get; set; } = "";
    public string EmpDivision { get; set; } = "";
    public string EmpDepartment { get; set; } = "";
    public string? ManagerCode { get; set; }
    public string? ManagerName { get; set; }
    public string ManagerDivision { get; set; } = "";
    public string ManagerDepartment { get; set; } = "";
    public string Reason { get; set; } = "";   // cross-division / cross-department / cross-both / dangling
}

public class ConsistencyResult
{
    public int Total { get; set; }
    public int Consistent { get; set; }
    public int NoManager { get; set; }
    public int Dangling { get; set; }
    public List<InconsistentRow> Inconsistent { get; set; } = new();
}

public class OrgMoveRow
{
    public string? Code { get; set; }
    public string Name { get; set; } = "";
    public string FromDivision { get; set; } = ""; public string ToDivision { get; set; } = "";
    public string FromDepartment { get; set; } = ""; public string ToDepartment { get; set; } = "";
    public string FromJob { get; set; } = ""; public string ToJob { get; set; } = "";
}

public class RestructureViewModel
{
    public OrgModelDto Model { get; set; } = new();
    public List<OrgVersion> Versions { get; set; } = new();
    public ConsistencyResult Consistency { get; set; } = new();
    public bool HasData { get; set; }
}
