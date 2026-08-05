using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Saudization.Web.Data;
using Saudization.Web.Models;

namespace Saudization.Web.Services;

/// <summary>
/// Builds a ready-to-fill import workbook that matches the chosen sheet rule. It is pre-populated with the
/// current reference data and the latest version's employees, so the user edits it and re-uploads as a new week.
/// </summary>
public class ExcelTemplateService
{
    private readonly AppDbContext _db;
    public ExcelTemplateService(AppDbContext db) => _db = db;

    private static readonly string[] EmpHeaders =
    {
        "رقم العامل","اسم العامل","الجنسية","الشركة","رقم المنشأة","إسم المنشأة",
        "رقم الحدود","الإقامة - البطاقة","المهنة","تاريخ انتهاء الاقامة","تاريخ دخول المملكة","نوع العامل"
    };

    public async Task<byte[]> BuildAsync(ImportSheetRule rule, CancellationToken ct = default)
    {
        var records = await _db.Records.Include(r => r.Facilities).OrderBy(r => r.RegistrationLabel).ToListAsync(ct);
        var categories = await _db.Categories.OrderBy(c => c.NameAr).ToListAsync(ct);
        var occupations = await _db.Occupations.Include(o => o.Category).OrderBy(o => o.NameAr).ToListAsync(ct);

        var latest = await _db.Snapshots.OrderByDescending(s => s.WeekDate).ThenByDescending(s => s.Id)
            .Select(s => (int?)s.Id).FirstOrDefaultAsync(ct);
        var employees = latest == null ? new List<Employee>() : await _db.Employees
            .Include(e => e.Nationality).Include(e => e.Facility!).ThenInclude(f => f.Record)
            .Where(e => e.SnapshotId == latest)
            .ToListAsync(ct);

        using var wb = new XLWorkbook();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ---- Records ----
        var wsR = wb.Worksheets.Add(SheetName("Records", used));
        WriteRow(wsR, 1, "إسم المنشأة", "اسم السجل", "السجل التجاري", "رقم السجل التجاري",
            "احمر", "اخضر منخفض", "أخضر متوسط", "أخضر مرتفع", "بلاتيني", "النسبة المطلوبة");
        int rr = 2;
        foreach (var rec in records)
            foreach (var f in rec.Facilities.OrderBy(f => f.Code))
                WriteRow(wsR, rr++, f.Code, rec.NameAr, rec.RegistrationLabel, rec.RegistrationNumber,
                    rec.LowGreenMin, rec.LowGreenMin, rec.MediumGreenMin, rec.HighGreenMin, rec.PlatinumMin, rec.RequiredPct);

        // ---- categories ----
        var wsC = wb.Worksheets.Add(SheetName("فئات التوطين", used));
        WriteRow(wsC, 1, "الفئة", "الحد الادنى لحساب التوطين", "النسبة الحالية", "تاريخ تطبيق القرار",
            "نسبة التوطين المستقبلية", "تاريخ تطبيق القرار الممستقبلي", "الحد الادنى للأجور");
        int cr = 2;
        foreach (var c in categories)
            WriteRow(wsC, cr++, c.NameAr, c.MinForCalc, c.CurrentRequiredPct, c.CurrentDecisionDate,
                c.FutureRequiredPct, c.FutureDecisionDate, c.MinWage);

        // ---- occupation guide ----
        var wsO = wb.Worksheets.Add(SheetName("دليل المهن", used));
        WriteRow(wsO, 1, "الرمز", "المهنة", "الفئة");
        int or = 2;
        foreach (var o in occupations) WriteRow(wsO, or++, o.Code, o.NameAr, o.Category?.NameAr);

        // ---- special / disability (headers only) ----
        var wsS = wb.Worksheets.Add(SheetName("الفئة الخاصة", used));
        WriteRow(wsS, 1, "#", "رقم الهوية", "الاسم", "الفئة الخاصة", "المهنة");
        var wsD = wb.Worksheets.Add(SheetName("ذوي الاحتياجات الخاصة", used));
        WriteRow(wsD, 1, "#", "رقم الهوية", "الاسم", "ليُحتسب بعدد");

        // ---- employee sheets per the chosen rule ----
        if (rule == ImportSheetRule.PerRegistration)
        {
            foreach (var rec in records)
            {
                var ws = wb.Worksheets.Add(SheetName(rec.RegistrationLabel, used));
                WriteHeaders(ws);
                int row = 2;
                foreach (var e in employees.Where(e => e.Facility?.RecordId == rec.Id).OrderBy(e => e.Name))
                    WriteEmployee(ws, row++, e);
            }
        }
        else
        {
            foreach (var rec in records)
                foreach (var f in rec.Facilities.OrderBy(f => f.Code))
                {
                    var ws = wb.Worksheets.Add(SheetName(f.Code, used));
                    WriteHeaders(ws);
                    int row = 2;
                    foreach (var e in employees.Where(e => e.FacilityId == f.Id).OrderBy(e => e.Name))
                        WriteEmployee(ws, row++, e);
                }
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void WriteHeaders(IXLWorksheet ws)
    {
        for (int i = 0; i < EmpHeaders.Length; i++) ws.Cell(1, i + 1).Value = EmpHeaders[i];
        ws.Row(1).Style.Font.Bold = true;
    }

    private static void WriteEmployee(IXLWorksheet ws, int row, Employee e)
    {
        WriteRow(ws, row, e.WorkerNo, e.Name, e.Nationality?.NameAr, e.Location, e.Facility?.Code,
            e.Facility?.NameAr, e.BorderNo, e.IqamaNo, e.OccupationRaw, e.IqamaExpiry, e.EntryDate, e.WorkerType);
    }

    private static void WriteRow(IXLWorksheet ws, int row, params object?[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            var v = values[i];
            if (v is null) continue;
            var cell = ws.Cell(row, i + 1);
            switch (v)
            {
                case decimal d: cell.Value = d; break;
                case int n: cell.Value = n; break;
                default: cell.Value = v.ToString(); break;
            }
        }
        if (row == 1) ws.Row(1).Style.Font.Bold = true;
    }

    // Excel: ≤31 chars, no []:*?/\ , unique.
    private static string SheetName(string raw, HashSet<string> used)
    {
        var s = new string((raw ?? "Sheet").Where(ch => "[]:*?/\\".IndexOf(ch) < 0).ToArray()).Trim();
        if (s.Length == 0) s = "Sheet";
        if (s.Length > 31) s = s[..31];
        var baseName = s; int i = 2;
        while (used.Contains(s)) { var suffix = " (" + i++ + ")"; s = baseName[..Math.Min(baseName.Length, 31 - suffix.Length)] + suffix; }
        used.Add(s);
        return s;
    }
}
