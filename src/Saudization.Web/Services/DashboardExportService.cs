using ClosedXML.Excel;
using Saudization.Web.Models;

namespace Saudization.Web.Services;

/// <summary>Renders the Dashboard overview (KPIs, per-registration bars, findings, suggestions, trend) to a
/// colored .xlsx that mirrors the on-screen design — same zone palette, KPI tones, and a data-bar for the %.</summary>
public class DashboardExportService
{
    private const string Ink = "#1B2733", Muted = "#6B7A8D", Line = "#E2E8F0", Brand = "#0B6B3A", Head = "#0B6B3A";

    private static string Tone(string t) => t switch { "ok" => "#2F9E44", "warn" => "#B45309", "bad" => "#D64545", _ => Ink };
    private static string SevColor(FindingSeverity s) => s switch { FindingSeverity.High => "#D64545", FindingSeverity.Medium => "#E8890C", _ => "#3B82F6" };

    public byte[] Build(DashboardOverviewViewModel o, Func<string, string> t, bool isRtl)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(t("Nav_Dashboard"));
        if (isRtl) ws.RightToLeft = true;
        string Pct(decimal p) => (p * 100).ToString("0.#") + "%";

        int row = 1;
        var title = ws.Cell(row, 1);
        title.Value = t("Nav_Dashboard") + (o.Snapshot != null ? "  —  " + o.Snapshot.Label + " · " + o.Snapshot.WeekDate.ToString("yyyy-MM-dd") : "");
        title.Style.Font.Bold = true; title.Style.Font.FontSize = 15; title.Style.Font.FontColor = XLColor.FromHtml(Brand);
        row += 2;

        // ---- KPI tiles: 6 blocks, 2 columns each, 3 rows tall ----
        Section(ws, row - 1, t("Ov_Kpis"));
        int vRow = row, lRow = row + 1, sRow = row + 2, ki = 0;
        foreach (var k in o.Kpis)
        {
            int c = 1 + 2 * ki;
            var v = ws.Cell(vRow, c); ws.Range(vRow, c, vRow, c + 1).Merge();
            v.Value = k.Value; v.Style.Font.Bold = true; v.Style.Font.FontSize = 18; v.Style.Font.FontColor = XLColor.FromHtml(Tone(k.Tone));
            v.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            var l = ws.Cell(lRow, c); ws.Range(lRow, c, lRow, c + 1).Merge();
            l.Value = t(k.LabelKey); l.Style.Font.FontColor = XLColor.FromHtml(Muted); l.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            var s = ws.Cell(sRow, c); ws.Range(sRow, c, sRow, c + 1).Merge();
            s.Value = k.Sub ?? ""; s.Style.Font.FontColor = XLColor.FromHtml(Muted); s.Style.Font.FontSize = 10; s.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            var box = ws.Range(vRow, c, sRow, c + 1);
            box.Style.Border.OutsideBorder = XLBorderStyleValues.Thin; box.Style.Border.OutsideBorderColor = XLColor.FromHtml(Line);
            ki++;
        }
        row = sRow + 2;

        // ---- By registration (bars via data-bar on the % column) ----
        Section(ws, row - 1, t("Ov_ByRegistration"));
        string[] rh = { t("Dash_Registration"), t("Dash_Zone"), t("Dash_SaudiPct"), t("Dash_Saudi"), t("Dash_Total"), t("Dash_NonSaudi"), t("Dash_Disabled"), t("Dash_Required") };
        HeaderRow(ws, row, rh);
        int firstData = row + 1;
        foreach (var rc in o.Records)
        {
            row++;
            ws.Cell(row, 1).Value = rc.Label;
            var z = ws.Cell(row, 2); z.Value = t(ZoneDisplay.Key(rc.Zone));
            z.Style.Fill.BackgroundColor = XLColor.FromHtml(rc.ZoneColor ?? ZoneDisplay.DefaultColor(rc.Zone));
            z.Style.Font.FontColor = XLColor.White; z.Style.Font.Bold = true; z.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            var p = ws.Cell(row, 3); p.Value = Math.Round(rc.Pct * 100, 1); p.Style.NumberFormat.Format = "0.0\"%\"";
            ws.Cell(row, 4).Value = rc.SaudiCountedGeneral;
            ws.Cell(row, 5).Value = rc.Headcount;
            ws.Cell(row, 6).Value = rc.NonSaudi;
            ws.Cell(row, 7).Value = Pct(rc.DisabledPct);
            ws.Cell(row, 8).Value = rc.RequiredPct > 0 ? Pct(rc.RequiredPct) : "—";
        }
        if (o.Records.Count > 0)
        {
            var pctRange = ws.Range(firstData, 3, row, 3);
            pctRange.AddConditionalFormat().DataBar(XLColor.FromHtml("#7BB661"))
                .Minimum(XLCFContentType.Number, 0).Maximum(XLCFContentType.Number, 100);
            ws.Range(firstData, 1, row, rh.Length).Style.Border.InsideBorder = XLBorderStyleValues.Hair;
        }
        row += 2;

        // ---- Findings ----
        Section(ws, row - 1, t("Ov_Findings"));
        if (o.Findings.Count == 0) { ws.Cell(row, 1).Value = "✓ " + t("Ov_NoFindings"); row++; }
        else
            foreach (var f in o.Findings)
            {
                var d = ws.Cell(row, 1); d.Value = "●"; d.Style.Font.FontColor = XLColor.FromHtml(SevColor(f.Severity)); d.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                var txt = ws.Cell(row, 2); ws.Range(row, 2, row, 8).Merge(); txt.Value = FindingText(f, t, Pct);
                row++;
            }
        row += 1;

        // ---- Suggestions ----
        Section(ws, row - 1, t("Ov_Suggestions"));
        if (o.Suggestions.Count == 0) { ws.Cell(row, 1).Value = t("Tr_None"); row++; }
        else
            foreach (var s in o.Suggestions)
            {
                var c = ws.Cell(row, 1); ws.Range(row, 1, row, 8).Merge();
                c.Value = $"{s.CategoryName} — {(s.BringSaudi ? t("Tr_BringSaudi") : t("Tr_MoveNonSaudi"))}: {s.FromLabel} → {s.ToLabel}  ({Pct(s.ToPctBefore)} → {Pct(s.ToPctAfter)})";
                row++;
            }
        row += 1;

        // ---- Trend across versions ----
        if (o.OverallTrend != null && o.TrendSnaps.Count > 0)
        {
            Section(ws, row - 1, t("Ver_Trend"));
            HeaderRow(ws, row, new[] { t("Dash_Version"), t("Dash_SaudiPct"), t("Dash_Zone") });
            for (int i = 0; i < o.TrendSnaps.Count && i < o.OverallTrend.Points.Count; i++)
            {
                row++;
                var pt = o.OverallTrend.Points[i];
                ws.Cell(row, 1).Value = o.TrendSnaps[i].Label;
                var pc = ws.Cell(row, 2); pc.Value = Math.Round(pt.Pct * 100, 1); pc.Style.NumberFormat.Format = "0.0\"%\"";
                var z = ws.Cell(row, 3); z.Value = t(ZoneDisplay.Key(pt.Zone));
                z.Style.Font.FontColor = XLColor.FromHtml(ZoneDisplay.DefaultColor(pt.Zone)); z.Style.Font.Bold = true;
            }
        }

        for (int c = 1; c <= 12; c++) ws.Column(c).Width = c == 1 ? 22 : 13;
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void Section(IXLWorksheet ws, int row, string label)
    {
        var c = ws.Cell(row, 1); c.Value = label; c.Style.Font.Bold = true; c.Style.Font.FontSize = 12; c.Style.Font.FontColor = XLColor.FromHtml(Ink);
    }
    private static void HeaderRow(IXLWorksheet ws, int row, string[] heads)
    {
        for (int i = 0; i < heads.Length; i++) ws.Cell(row, i + 1).Value = heads[i];
        var r = ws.Range(row, 1, row, heads.Length);
        r.Style.Font.Bold = true; r.Style.Fill.BackgroundColor = XLColor.FromHtml(Head); r.Style.Font.FontColor = XLColor.White;
    }
    private static string FindingText(Finding f, Func<string, string> t, Func<decimal, string> Pct) => f.Kind switch
    {
        FindingKind.RedRegistration => string.Format(t("Find_RedReg"), f.Name, Pct(f.Pct)),
        FindingKind.RedCategoryCell => string.Format(t("Find_RedCell"), f.Name, f.Name2, f.Count),
        FindingKind.UnmappedOccupations => string.Format(t("Find_Unmapped"), f.Count),
        FindingKind.RegistrationNoRequired => string.Format(t("Find_NoReq"), f.Name),
        _ => ""
    };
}
