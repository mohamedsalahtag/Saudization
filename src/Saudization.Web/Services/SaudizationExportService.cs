using ClosedXML.Excel;
using Saudization.Web.Models;

namespace Saudization.Web.Services;

/// <summary>Renders one or more Saudization pivot grids (original + enhanced) to a colored .xlsx that mirrors
/// the on-screen design — same zone/compliance palette, one worksheet per grid.</summary>
public class SaudizationExportService
{
    // compliance-cell fills (match .cell-* in app.css)
    private static string StatusFill(CellStatus s) => s switch
    {
        CellStatus.Red => "#FDECEC",
        CellStatus.LightGreen => "#EEF7E8",
        CellStatus.HighGreen => "#E6F4EA",
        CellStatus.Exempt => "#EEF3F7",
        _ => "#FFFFFF"
    };

    public byte[] Build(IReadOnlyList<(string title, DashboardViewModel vm)> grids, Func<string, string> t, bool isRtl)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Saudization");
        if (isRtl) ws.RightToLeft = true;
        int top = 1;
        foreach (var (title, vm) in grids)
        {
            var tc = ws.Cell(top, 1);
            tc.Value = title; tc.Style.Font.Bold = true; tc.Style.Font.FontSize = 13;
            top = RenderBlock(ws, top + 1, vm, t, isRtl) + 2;   // block + one blank row between the two grids
        }
        if (grids.Count == 2)
            RenderChanges(ws, top, grids[0].vm, grids[1].vm, t, isRtl);
        ws.Columns().AdjustToContents();
        ws.Column(1).Width = Math.Min(ws.Column(1).Width, 34);
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>A clean "what changed" table below the two grids — only the (registration × category) cells
    /// whose Saudi/non-Saudi counts differ between original and enhanced, with before→after and deltas.</summary>
    private static void RenderChanges(IXLWorksheet ws, int top, DashboardViewModel orig, DashboardViewModel enh, Func<string, string> t, bool isRtl)
    {
        string Pct(decimal p) => (p * 100).ToString("0.#") + "%";
        string Cat(CategoryRow r) => isRtl ? r.NameAr : (string.IsNullOrWhiteSpace(r.NameEn) ? r.NameAr : r.NameEn!);
        var origByCat = orig.Rows.ToDictionary(r => r.CategoryId ?? -1);
        var recLabel = enh.Records.ToDictionary(r => r.RecordId, r => r.Label);

        var title = ws.Cell(top, 1); title.Value = t("Dash_Changes"); title.Style.Font.Bold = true; title.Style.Font.FontSize = 13;
        int r1 = top + 1;
        string[] heads = { t("Dash_Registration"), t("Dash_Category"), t("Dash_NonSaudi") + " " + t("Dash_Was"), t("Dash_NonSaudi") + " " + t("Dash_Now"), "Δ",
            t("Dash_Saudi") + " " + t("Dash_Was"), t("Dash_Saudi") + " " + t("Dash_Now"), "Δ", t("Dash_Pct") + " " + t("Dash_Was"), t("Dash_Pct") + " " + t("Dash_Now") };
        for (int i = 0; i < heads.Length; i++) ws.Cell(r1, i + 1).Value = heads[i];
        var hdr = ws.Range(r1, 1, r1, heads.Length);
        hdr.Style.Font.Bold = true; hdr.Style.Fill.BackgroundColor = XLColor.FromHtml("#0B6B3A"); hdr.Style.Font.FontColor = XLColor.White;

        int row = r1 + 1;
        foreach (var er in enh.Rows)
        {
            if (!origByCat.TryGetValue(er.CategoryId ?? -1, out var or)) continue;
            foreach (var rc in enh.Records)
            {
                var oc = or.ByRecord.TryGetValue(rc.RecordId, out var a) ? a : new Cell();
                var ec = er.ByRecord.TryGetValue(rc.RecordId, out var b) ? b : new Cell();
                if (oc.Saudi == ec.Saudi && oc.NonSaudi == ec.NonSaudi) continue;   // only changes
                int dN = ec.NonSaudi - oc.NonSaudi, dS = ec.Saudi - oc.Saudi;
                ws.Cell(row, 1).Value = recLabel.GetValueOrDefault(rc.RecordId, "");
                ws.Cell(row, 2).Value = Cat(er);
                ws.Cell(row, 3).Value = oc.NonSaudi; ws.Cell(row, 4).Value = ec.NonSaudi;
                var cN = ws.Cell(row, 5); cN.Value = (dN > 0 ? "+" : "") + dN; cN.Style.Font.FontColor = XLColor.FromHtml(dN <= 0 ? "#2F9E44" : "#D64545");
                ws.Cell(row, 6).Value = oc.Saudi; ws.Cell(row, 7).Value = ec.Saudi;
                var cS = ws.Cell(row, 8); cS.Value = (dS > 0 ? "+" : "") + dS; cS.Style.Font.FontColor = XLColor.FromHtml(dS >= 0 ? "#2F9E44" : "#D64545");
                ws.Cell(row, 9).Value = Pct(oc.Pct);
                var pn = ws.Cell(row, 10); pn.Value = Pct(ec.Pct);
                if (ec.Status != CellStatus.NA) pn.Style.Fill.BackgroundColor = XLColor.FromHtml(StatusFill(ec.Status));
                row++;
            }
        }
        if (row == r1 + 1) { ws.Cell(row, 1).Value = t("Dash_NoChanges"); row++; }
        var block = ws.Range(r1, 1, row - 1, heads.Length);
        block.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        block.Style.Border.InsideBorder = XLBorderStyleValues.Hair;
    }

    /// <summary>Writes one pivot grid starting at row <paramref name="r1"/>; returns the totals row.</summary>
    private static int RenderBlock(IXLWorksheet ws, int r1, DashboardViewModel vm, Func<string, string> t, bool isRtl)
    {
        var recs = vm.Records;
        string Pct(decimal p) => (p * 100).ToString("0.#") + "%";
        string Cat(CategoryRow r) => isRtl ? r.NameAr : (string.IsNullOrWhiteSpace(r.NameEn) ? r.NameAr : r.NameEn!);

        int r2 = r1 + 1;             // two header rows
        var sepCols = new List<int>();   // first column of each record group (+ grand) → medium left border
        // fixed left columns
        ws.Cell(r1, 1).Value = t("Dash_Category"); ws.Range(r1, 1, r2, 1).Merge();
        ws.Cell(r1, 2).Value = t("Dash_MinCalc"); ws.Range(r1, 2, r2, 2).Merge();
        ws.Cell(r1, 3).Value = t("Dash_Required"); ws.Range(r1, 3, r2, 3).Merge();

        int col = 4;
        var recColStart = new Dictionary<int, int>();
        foreach (var rc in recs)
        {
            recColStart[rc.RecordId] = col;
            sepCols.Add(col);
            // Action column is intentionally NOT exported — the exported grid is the numbers only.
            ws.Range(r1, col, r1, col + 3).Merge();
            var h = ws.Cell(r1, col);
            h.Value = rc.Label + "  —  " + t(ZoneDisplay.Key(rc.Zone)) + " " + Pct(rc.Pct);
            h.Style.Fill.BackgroundColor = XLColor.FromHtml(rc.ZoneColor ?? ZoneDisplay.DefaultColor(rc.Zone));
            h.Style.Font.FontColor = XLColor.White; h.Style.Font.Bold = true;
            h.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(r2, col).Value = t("Dash_NonSaudi");
            ws.Cell(r2, col + 1).Value = t("Dash_Saudi");
            ws.Cell(r2, col + 2).Value = t("Dash_Total");
            ws.Cell(r2, col + 3).Value = t("Dash_Pct");
            col += 4;
        }
        int grandCol = col;
        sepCols.Add(grandCol);
        ws.Range(r1, grandCol, r1, grandCol + 3).Merge();
        ws.Cell(r1, grandCol).Value = t("Dash_GrandTotal");
        ws.Cell(r2, grandCol).Value = t("Dash_NonSaudi");
        ws.Cell(r2, grandCol + 1).Value = t("Dash_Saudi");
        ws.Cell(r2, grandCol + 2).Value = t("Dash_Total");
        ws.Cell(r2, grandCol + 3).Value = t("Dash_Pct");
        ws.Range(r1, 1, r2, grandCol + 3).Style.Font.Bold = true;
        ws.Range(r1, 1, r2, grandCol + 3).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        int row = r2 + 1;
        foreach (var cr in vm.Rows)
        {
            ws.Cell(row, 1).Value = Cat(cr);
            if (!cr.IsUnclassified) { ws.Cell(row, 2).Value = cr.MinForCalc; ws.Cell(row, 3).Value = Pct(cr.RequiredPct); }
            foreach (var rc in recs)
            {
                var c = cr.ByRecord.TryGetValue(rc.RecordId, out var cell) ? cell : new Cell();
                int cc = recColStart[rc.RecordId];
                ws.Cell(row, cc).Value = c.NonSaudi;
                ws.Cell(row, cc + 1).Value = c.Saudi;
                ws.Cell(row, cc + 2).Value = c.Total;
                var pctCell = ws.Cell(row, cc + 3);
                pctCell.Value = Pct(c.Pct);
                if (c.Status != CellStatus.NA) pctCell.Style.Fill.BackgroundColor = XLColor.FromHtml(StatusFill(c.Status));
            }
            ws.Cell(row, grandCol).Value = cr.Grand.NonSaudi;
            ws.Cell(row, grandCol + 1).Value = cr.Grand.Saudi;
            ws.Cell(row, grandCol + 2).Value = cr.Grand.Total;
            ws.Cell(row, grandCol + 3).Value = Pct(cr.Grand.Pct);
            row++;
        }

        // totals row (general, weighted)
        ws.Cell(row, 1).Value = t("Dash_GrandTotal");
        foreach (var rc in recs)
        {
            int cc = recColStart[rc.RecordId];
            ws.Cell(row, cc).Value = rc.NonSaudi;
            ws.Cell(row, cc + 1).Value = rc.BaseSaudi;
            ws.Cell(row, cc + 2).Value = rc.Headcount;
            ws.Cell(row, cc + 3).Value = Pct(rc.Pct);
        }
        ws.Cell(row, grandCol).Value = vm.GrandHeadcount - vm.GrandBaseSaudi;
        ws.Cell(row, grandCol + 1).Value = vm.GrandBaseSaudi;
        ws.Cell(row, grandCol + 2).Value = vm.GrandHeadcount;
        ws.Cell(row, grandCol + 3).Value = Pct(vm.GrandPct);
        ws.Row(row).Style.Font.Bold = true;

        // block borders: thin grid + a medium separator on the first column of each registration group
        var block = ws.Range(r1, 1, row, grandCol + 3);
        block.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        block.Style.Border.InsideBorder = XLBorderStyleValues.Hair;
        foreach (var sc in sepCols)
            ws.Range(r1, sc, row, sc).Style.Border.LeftBorder = XLBorderStyleValues.Medium;
        return row;
    }

    private static string ActionText(Cell c, Func<string, string> t) => c.Status switch
    {
        CellStatus.Red => $"↔ {c.ReplaceNeeded} {t("Dash_Replace")} · ＋ {c.HireNeeded} {t("Dash_Hire")} · ⤓ {c.ShedNonSaudi} {t("Dash_Shed")}",
        CellStatus.LightGreen or CellStatus.HighGreen when (c.AbsorbNonSaudi ?? 0) > 0 || (c.SaudiSurplus ?? 0) > 0
            => $"⤒ +{c.AbsorbNonSaudi} {t("Dash_Absorb")} · ▽ {c.SaudiSurplus} {t("Dash_Surplus")}",
        CellStatus.Exempt => $"✓ {t("Dash_Exempt")} · +{c.ExemptAddNonSaudi} {t("Dash_AddBeforeCalc")}",
        _ => ""
    };
}
