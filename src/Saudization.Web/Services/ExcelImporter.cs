using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Saudization.Web.Data;
using Saudization.Web.Models;

namespace Saudization.Web.Services;

public class ImportResult
{
    public int Records, Facilities, Employees, Categories, Occupations, Nationalities, Disabilities, SpecialCases;
    public int OccupationsUnmatched;
    public int OccupationsUpdated, EmployeesRelinked, GuideRows;
    public int SpecialMatched, SpecialUnmatched, DisabilityMatched, DisabilityUnmatched;
    public readonly List<string> Warnings = new();
    public bool Success => Warnings.All(w => !w.StartsWith("ERROR"));
}

/// <summary>
/// Reads a dashboard.xlsx-shaped workbook (reference sheets + one sheet per facility code)
/// and upserts everything into the database. Matching is by Arabic header text so a
/// re-exported file with reordered columns still loads.
/// </summary>
public class ExcelImporter
{
    private readonly AppDbContext _db;
    public ExcelImporter(AppDbContext db) => _db = db;

    // Sheet names in the source workbook (Arabic).
    private const string ShRecords = "Records";
    private const string ShCategories = "فئات التوطين";
    private const string ShOccupations = "دليل المهن";
    private const string ShSpecial = "الفئة الخاصة";
    private const string ShDisability = "ذوي الاحتياجات الخاصة";

    /// <summary>Imports reference data (shared across versions) and loads all employees into <paramref name="snapshot"/>.</summary>
    public async Task<ImportResult> ImportAsync(Stream xlsx, Snapshot snapshot,
        ImportSheetRule rule = ImportSheetRule.PerFacility, CancellationToken ct = default)
    {
        var res = new ImportResult();
        using var wb = new XLWorkbook(xlsx);

        var catByName = await ImportCategoriesAsync(wb, res, ct);
        var occByName = await ImportOccupationsAsync(wb, res, catByName, ct);
        var recByLabel = await ImportRecordsAsync(wb, res, ct);        // also creates facilities
        await ImportReferenceListsAsync(wb, res, ct);                  // disability + special (raw lists)
        await ImportEmployeesAsync(wb, res, occByName, snapshot, rule, recByLabel, ct);
        ApplyWeighting(wb, res, snapshot);                            // §4/§5: match special/disabled → employees

        await _db.SaveChangesAsync(ct);
        return res;
    }

    // ---------- categories (فئات التوطين) ----------
    private async Task<Dictionary<string, SaudizationCategory>> ImportCategoriesAsync(
        XLWorkbook wb, ImportResult res, CancellationToken ct)
    {
        var map = await _db.Categories.ToDictionaryAsync(c => c.NameAr, ct);
        var ws = Find(wb, ShCategories);
        if (ws is null) { res.Warnings.Add($"Sheet '{ShCategories}' not found — categories skipped."); return map; }

        var h = HeaderIndex(ws);
        int cName = Col(h, "الفئة");
        int cMin = Col(h, "الحد الادنى لحساب التوطين", "الحد الأدنى لحساب التوطين");
        int cCur = Col(h, "النسبة الحالية");
        int cCurD = Col(h, "تاريخ تطبيق القرار");
        int cFut = Col(h, "نسبة التوطين المستقبلية", "النسبة المستقبلية");
        int cFutD = Col(h, "تاريخ تطبيق القرار الممستقبلي", "تاريخ تطبيق القرار المستقبلي");
        int cWage = Col(h, "الحد الادنى للأجور", "الحد الأدنى للأجور");

        foreach (var row in DataRows(ws))
        {
            var name = Str(row, cName);
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (!map.TryGetValue(name, out var cat))
            {
                cat = new SaudizationCategory { NameAr = name };
                _db.Categories.Add(cat); map[name] = cat; res.Categories++;
            }
            cat.MinForCalc = (int)(Num(row, cMin) ?? 0);
            // Frac normalizes a whole-number percentage (e.g. 30 → 0.30) so it fits decimal(5,4); true fractions pass through.
            cat.CurrentRequiredPct = Frac(row, cCur) ?? 0;
            cat.CurrentDecisionDate = Str(row, cCurD);
            cat.FutureRequiredPct = Frac(row, cFut) ?? cat.CurrentRequiredPct;
            cat.FutureDecisionDate = Str(row, cFutD);
            cat.MinWage = Num(row, cWage);
        }
        return map;
    }

    // ---------- occupation guide (دليل المهن) ----------
    private async Task<Dictionary<string, Occupation>> ImportOccupationsAsync(
        XLWorkbook wb, ImportResult res, Dictionary<string, SaudizationCategory> cats, CancellationToken ct)
    {
        var map = await _db.Occupations.ToDictionaryAsync(o => o.NameAr, ct);
        var ws = Find(wb, ShOccupations);
        if (ws is null) { res.Warnings.Add($"Sheet '{ShOccupations}' not found — occupation guide skipped."); return map; }

        var h = HeaderIndex(ws);
        int cCode = Col(h, "الرمز");
        int cName = Col(h, "المهنة");
        int cCat = Col(h, "الفئة");

        foreach (var row in DataRows(ws))
        {
            var name = Str(row, cName);
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (!map.TryGetValue(name, out var occ))
            {
                occ = new Occupation { NameAr = name };
                _db.Occupations.Add(occ); map[name] = occ; res.Occupations++;
            }
            occ.Code = Str(row, cCode);
            var catName = Str(row, cCat);
            if (!string.IsNullOrWhiteSpace(catName) && cats.TryGetValue(catName!, out var cat))
                occ.Category = cat;
        }
        return map;
    }

    /// <summary>Applies a standalone occupation-guide workbook (columns الرمز / المهنة / الفئة, any sheet) to the
    /// shared reference data: creates missing categories, upserts occupations + their category, and back-fills the
    /// occupation link on every employee (all versions) whose raw occupation now matches the guide.</summary>
    public async Task<ImportResult> ApplyOccupationGuideAsync(Stream xlsx, CancellationToken ct = default)
    {
        var res = new ImportResult();
        using var wb = new XLWorkbook(xlsx);
        var ws = Find(wb, ShOccupations) ?? FindByHeaders(wb, "المهنة", "الفئة") ?? wb.Worksheets.FirstOrDefault();
        if (ws is null) { res.Warnings.Add("ERROR: no worksheet found in the file."); return res; }

        var h = HeaderIndex(ws);
        int cCode = Col(h, "الرمز"), cName = Col(h, "المهنة"), cCat = Col(h, "الفئة");
        if (cName < 1 || cCat < 1) { res.Warnings.Add("ERROR: the sheet must have المهنة (occupation) and الفئة (category) columns."); return res; }

        var catByNorm = (await _db.Categories.ToListAsync(ct))
            .GroupBy(c => Normalize(c.NameAr)).ToDictionary(g => g.Key, g => g.First());
        var occByNorm = (await _db.Occupations.ToListAsync(ct))
            .GroupBy(o => Normalize(o.NameAr)).ToDictionary(g => g.Key, g => g.First());

        foreach (var row in DataRows(ws))
        {
            var name = Str(row, cName);
            if (string.IsNullOrWhiteSpace(name)) continue;
            res.GuideRows++;

            SaudizationCategory? cat = null;
            var catName = Str(row, cCat);
            if (!string.IsNullOrWhiteSpace(catName))
            {
                var cnorm = Normalize(catName!);
                if (!catByNorm.TryGetValue(cnorm, out cat))
                { cat = new SaudizationCategory { NameAr = catName!.Trim() }; _db.Categories.Add(cat); catByNorm[cnorm] = cat; res.Categories++; }
            }

            var nnorm = Normalize(name!);
            if (!occByNorm.TryGetValue(nnorm, out var occ))
            { occ = new Occupation { NameAr = name!.Trim() }; _db.Occupations.Add(occ); occByNorm[nnorm] = occ; res.Occupations++; }
            else res.OccupationsUpdated++;

            var code = Str(row, cCode);
            if (!string.IsNullOrWhiteSpace(code)) occ.Code = code;
            if (cat != null) occ.Category = cat;
        }
        await _db.SaveChangesAsync(ct);   // assign new occupation ids

        // back-fill the occupation link on employees that had no mapping before
        var emps = await _db.Employees.Where(e => e.OccupationRaw != null && e.OccupationId == null).ToListAsync(ct);
        foreach (var e in emps)
            if (occByNorm.TryGetValue(Normalize(e.OccupationRaw!), out var occ)) { e.Occupation = occ; res.EmployeesRelinked++; }
        await _db.SaveChangesAsync(ct);

        return res;
    }

    private static IXLWorksheet? FindByHeaders(XLWorkbook wb, params string[] required)
    {
        foreach (var w in wb.Worksheets)
        {
            var h = HeaderIndex(w);
            if (required.All(r => Col(h, r) > 0)) return w;
        }
        return null;
    }

    // ---------- records + facilities (Records sheet) ----------
    private async Task<Dictionary<string, Record>> ImportRecordsAsync(
        XLWorkbook wb, ImportResult res, CancellationToken ct)
    {
        var recByLabel = await _db.Records.ToDictionaryAsync(r => r.RegistrationLabel, ct);
        var facByCode = await _db.Facilities.ToDictionaryAsync(f => f.Code, ct);

        var ws = Find(wb, ShRecords);
        if (ws is null) { res.Warnings.Add($"Sheet '{ShRecords}' not found — records skipped."); return recByLabel; }

        var h = HeaderIndex(ws);
        int cFac = Col(h, "إسم المنشأة", "اسم المنشأة", "رقم المنشأة");
        int cName = Col(h, "اسم السجل");
        int cLabel = Col(h, "السجل التجاري");
        int cRed = Col(h, "احمر", "أحمر");
        int cMed = Col(h, "أخضر متوسط", "اخضر متوسط");   // 5th zone — usually absent in ministry files
        int cHigh = Col(h, "أخضر مرتفع", "اخضر مرتفع");
        int cPlat = Col(h, "بلاتيني");

        foreach (var row in DataRows(ws))
        {
            var facCode = Str(row, cFac);
            var label = Str(row, cLabel);
            if (string.IsNullOrWhiteSpace(label)) continue;

            if (!recByLabel.TryGetValue(label!, out var rec))
            {
                rec = new Record { RegistrationLabel = label! };
                _db.Records.Add(rec); recByLabel[label!] = rec; res.Records++;
            }
            var recName = Str(row, cName);
            if (!string.IsNullOrWhiteSpace(recName)) rec.NameAr = recName;

            var red = Frac(row, cRed); if (red is not null) rec.LowGreenMin = red.Value;
            var high = Frac(row, cHigh); if (high is not null) rec.HighGreenMin = high.Value;
            var plat = Frac(row, cPlat); if (plat is not null) rec.PlatinumMin = plat.Value;
            // Keep the (spec-added) medium-green threshold between low and high so all five zones stay ascending.
            var med = Frac(row, cMed);
            rec.MediumGreenMin = med ?? (rec.LowGreenMin + rec.HighGreenMin) / 2m;

            if (!string.IsNullOrWhiteSpace(facCode))
            {
                if (!facByCode.TryGetValue(facCode!, out var fac))
                {
                    fac = new Facility { Code = facCode! };
                    _db.Facilities.Add(fac); facByCode[facCode!] = fac; res.Facilities++;
                }
                fac.Record = rec;
                if (string.IsNullOrWhiteSpace(fac.NameAr)) fac.NameAr = recName;
            }
        }
        return recByLabel;
    }

    private async Task ImportReferenceListsAsync(XLWorkbook wb, ImportResult res, CancellationToken ct)
    {
        var disWs = Find(wb, ShDisability);
        if (disWs is not null)
        {
            var h = HeaderIndex(disWs);
            int cId = Col(h, "رقم الهوية");
            int cName = Col(h, "الاسم");
            int cCount = Col(h, "ليُحتسب بعدد", "ليحتسب بعدد");
            foreach (var row in DataRows(disWs))
            {
                var id = Str(row, cId); var nm = Str(row, cName);
                if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(nm)) continue;
                _db.DisabilityRecords.Add(new DisabilityRecord
                { IdNumber = id, Name = nm, CountAs = (int)(Num(row, cCount) ?? 4) });
                res.Disabilities++;
            }
        }

        var spWs = Find(wb, ShSpecial);
        if (spWs is not null)
        {
            var h = HeaderIndex(spWs);
            int cId = Col(h, "رقم الهوية");
            int cName = Col(h, "الاسم");
            int cCat = Col(h, "الفئة الخاصة");
            int cOcc = Col(h, "المهنة");
            foreach (var row in DataRows(spWs))
            {
                var id = Str(row, cId); var nm = Str(row, cName);
                if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(nm)) continue;
                _db.SpecialCases.Add(new SpecialCase
                { IdNumber = id, Name = nm, Category = Str(row, cCat), Occupation = Str(row, cOcc) });
                res.SpecialCases++;
            }
        }
        await Task.CompletedTask;
    }

    // ---------- §4/§5: attach special-category & disability weighting to this snapshot's employees ----------
    private void ApplyWeighting(XLWorkbook wb, ImportResult res, Snapshot snapshot)
    {
        // Build IqamaNo → employee(s) for the employees just added to this snapshot.
        var byId = new Dictionary<string, List<Employee>>();
        foreach (var e in _db.ChangeTracker.Entries<Employee>().Select(x => x.Entity))
        {
            if (!ReferenceEquals(e.Snapshot, snapshot)) continue;
            var key = IdKey(e.IqamaNo);
            if (key is null) continue;
            (byId.TryGetValue(key, out var l) ? l : byId[key] = new List<Employee>()).Add(e);
        }

        // Special categories (الفئة الخاصة) — count as Saudi in general AND occupation %.
        var sp = Find(wb, ShSpecial);
        if (sp is not null)
        {
            var h = HeaderIndex(sp);
            int cId = Col(h, "رقم الهوية");
            int cCat = Col(h, "الفئة الخاصة");
            foreach (var row in DataRows(sp))
            {
                var key = IdKey(Str(row, cId));
                if (key is null) continue;
                if (byId.TryGetValue(key, out var emps))
                {
                    foreach (var e in emps) { e.CountsAsSaudiSpecial = true; e.SpecialCategory = Str(row, cCat); }
                    res.SpecialMatched++;
                }
                else res.SpecialUnmatched++;
            }
        }

        // People with special needs (ذوو الاحتياجات الخاصة) — يُحتسب بعدد weight, general % only.
        var dis = Find(wb, ShDisability);
        if (dis is not null)
        {
            var h = HeaderIndex(dis);
            int cId = Col(h, "رقم الهوية");
            int cCount = Col(h, "ليُحتسب بعدد", "ليحتسب بعدد");
            foreach (var row in DataRows(dis))
            {
                var key = IdKey(Str(row, cId));
                if (key is null) continue;
                var countAs = (int)(Num(row, cCount) ?? 1);
                if (countAs < 1) countAs = 1;
                if (byId.TryGetValue(key, out var emps))
                {
                    foreach (var e in emps) e.DisabilityCountAs = countAs;
                    res.DisabilityMatched++;
                }
                else res.DisabilityUnmatched++;
            }
        }

        if (res.SpecialUnmatched > 0 || res.DisabilityUnmatched > 0)
            res.Warnings.Add($"Unmatched IDs — special: {res.SpecialUnmatched}, disability: {res.DisabilityUnmatched} (no employee with that ID in this version).");
    }

    private static string? IdKey(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var s = id.Trim().Replace(" ", "");
        return s.Length == 0 ? null : s;
    }

    // ---------- employees — one sheet per facility (default) OR one sheet per registration/company ----------
    private async Task ImportEmployeesAsync(
        XLWorkbook wb, ImportResult res, Dictionary<string, Occupation> occByName, Snapshot snapshot,
        ImportSheetRule rule, Dictionary<string, Record> recByLabel, CancellationToken ct)
    {
        var facByCode = await _db.Facilities.ToDictionaryAsync(f => f.Code, ct);
        foreach (var f in _db.ChangeTracker.Entries<Facility>().Select(e => e.Entity))
            facByCode[f.Code] = f;   // include facilities added in this same session
        var natByName = await _db.Nationalities.ToDictionaryAsync(n => n.NameAr, ct);
        var recByNorm = recByLabel.ToDictionary(kv => Normalize(kv.Key), kv => kv.Value);

        // reference sheets are never employee sheets
        var reserved = new[] { ShRecords, ShCategories, ShOccupations, ShSpecial, ShDisability }.Select(Normalize).ToHashSet();

        foreach (var ws in wb.Worksheets)
        {
            var sheetNorm = Normalize(ws.Name);
            if (reserved.Contains(sheetNorm)) continue;

            Facility? sheetFac = null;   // fixed facility (PerFacility rule)
            Record? sheetReg = null;     // registration (PerRegistration rule)
            if (rule == ImportSheetRule.PerRegistration)
            {
                if (!recByNorm.TryGetValue(sheetNorm, out sheetReg)) continue;   // only registration-named sheets
            }
            else
            {
                if (!facByCode.TryGetValue(ws.Name.Trim(), out sheetFac)) continue;   // only facility-code sheets
            }

            var h = HeaderIndex(ws);
            int cWorker = Col(h, "رقم العامل");
            int cName = Col(h, "اسم العامل");
            int cNat = Col(h, "الجنسية");
            int cLoc = Col(h, "الشركة");
            int cFacNo = Col(h, "رقم المنشأة");
            int cFacName = Col(h, "إسم المنشأة", "اسم المنشأة");
            int cBorder = Col(h, "رقم الحدود");
            int cIqama = Col(h, "الإقامة - البطاقة", "الاقامة - البطاقة", "الإقامة", "الاقامة");
            int cOcc = Col(h, "المهنة");
            int cExp = Col(h, "تاريخ انتهاء الاقامة", "تاريخ انتهاء الإقامة");
            int cEntry = Col(h, "تاريخ دخول المملكة");
            int cType = Col(h, "نوع العامل");

            foreach (var row in DataRows(ws))
            {
                var name = Str(row, cName);
                if (string.IsNullOrWhiteSpace(name)) continue;

                // resolve the target facility for this row
                Facility fac;
                if (rule == ImportSheetRule.PerRegistration)
                {
                    var facCode = Str(row, cFacNo) ?? sheetReg!.RegistrationLabel;   // fall back to one facility per registration
                    if (!facByCode.TryGetValue(facCode, out fac!))
                    {
                        fac = new Facility { Code = facCode, NameAr = Str(row, cFacName), Record = sheetReg };
                        _db.Facilities.Add(fac); facByCode[facCode] = fac; res.Facilities++;
                    }
                }
                else fac = sheetFac!;

                var natName = Str(row, cNat);
                Nationality? nat = null;
                if (!string.IsNullOrWhiteSpace(natName))
                {
                    if (!natByName.TryGetValue(natName!, out nat))
                    {
                        nat = new Nationality { NameAr = natName!, IsSaudi = IsSaudi(natName!) };
                        _db.Nationalities.Add(nat); natByName[natName!] = nat; res.Nationalities++;
                    }
                }

                var occRaw = Str(row, cOcc);
                Occupation? occ = null;
                if (!string.IsNullOrWhiteSpace(occRaw))
                {
                    if (!occByName.TryGetValue(occRaw!, out occ)) res.OccupationsUnmatched++;
                }

                _db.Employees.Add(new Employee
                {
                    Snapshot = snapshot,
                    WorkerNo = Str(row, cWorker),
                    Name = name!,
                    Nationality = nat,
                    Location = Str(row, cLoc),
                    Facility = fac,
                    BorderNo = Str(row, cBorder),
                    IqamaNo = Str(row, cIqama),
                    Occupation = occ,
                    OccupationRaw = occRaw,
                    IqamaExpiry = Str(row, cExp),
                    EntryDate = Str(row, cEntry),
                    WorkerType = Str(row, cType)
                });
                res.Employees++;
            }
        }
    }

    // ===================== helpers =====================

    private static bool IsSaudi(string nat) =>
        nat.Contains("سعود") && !nat.Contains("غير");

    private static IXLWorksheet? Find(XLWorkbook wb, string name) =>
        wb.Worksheets.FirstOrDefault(w => Normalize(w.Name) == Normalize(name));

    /// <summary>Map normalized header text → 1-based column number, from the first non-empty row.</summary>
    private static Dictionary<string, int> HeaderIndex(IXLWorksheet ws)
    {
        var dict = new Dictionary<string, int>();
        var headerRow = ws.RowsUsed().FirstOrDefault();
        if (headerRow is null) return dict;
        foreach (var cell in headerRow.CellsUsed())
        {
            var key = Normalize(cell.GetString());
            if (key.Length > 0 && !dict.ContainsKey(key)) dict[key] = cell.Address.ColumnNumber;
        }
        return dict;
    }

    private static int Col(Dictionary<string, int> h, params string[] candidates)
    {
        foreach (var c in candidates)
        {
            var key = Normalize(c);
            if (h.TryGetValue(key, out var idx)) return idx;
            // contains-match fallback
            foreach (var kv in h) if (kv.Key.Contains(key) || key.Contains(kv.Key)) return kv.Value;
        }
        return -1;
    }

    private static IEnumerable<IXLRangeRow> DataRows(IXLWorksheet ws)
    {
        var used = ws.RangeUsed();
        if (used is null) yield break;
        bool first = true;
        foreach (var row in used.RowsUsed())
        {
            if (first) { first = false; continue; }   // skip header
            yield return row;
        }
    }

    private static string? Str(IXLRangeRow row, int col)
    {
        if (col < 1) return null;
        var v = row.Cell(col).GetString().Trim();
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    private static decimal? Num(IXLRangeRow row, int col)
    {
        if (col < 1) return null;
        var cell = row.Cell(col);
        if (cell.TryGetValue<double>(out var d)) return (decimal)d;
        var s = cell.GetString();
        var m = Regex.Match(s, @"-?\d+(?:[.,]\d+)?");
        if (m.Success && decimal.TryParse(m.Value.Replace(',', '.'),
            NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) return v;
        return null;
    }

    /// <summary>Parse a threshold cell into a 0..1 fraction; a value &gt;1 (e.g. "20-40%") is treated as a percentage.</summary>
    private static decimal? Frac(IXLRangeRow row, int col)
    {
        var n = Num(row, col);
        if (n is null) return null;
        return n.Value > 1m ? n.Value / 100m : n.Value;
    }

    private static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Trim();
        // collapse whitespace, unify Arabic alef/ya/ta-marbuta variants for resilient matching
        s = Regex.Replace(s, @"\s+", " ");
        s = s.Replace('أ', 'ا').Replace('إ', 'ا').Replace('آ', 'ا') // أإآ → ا
             .Replace('ى', 'ي')  // ى → ي
             .Replace('ة', 'ه'); // ة → ه
        return s;
    }
}
