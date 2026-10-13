namespace Saudization.Web.Services;

/// <summary>
/// Inline-SVG icons for the Saudi / non-Saudi / total tallies, shared by the read-only Razor grid and the
/// editable grid's JSON model so both render identically. Self-contained (no external assets → CSP-safe).
/// A Settings toggle decides whether these icons or plain text labels are shown.
/// </summary>
public static class NationalityIcons
{
    public static string Svg(string kind) => kind switch
    {
        "saudi" =>   // white ghutra + black agal ring + short beard (Saudi national)
            "<svg viewBox='0 0 24 24' xmlns='http://www.w3.org/2000/svg'>" +
            "<path d='M5 21v-1c0-3.3 3.1-5 7-5s7 1.7 7 5v1z' fill='#e9edf1'/>" +
            "<path d='M5.5 6.5C5.5 4 8.4 2.2 12 2.2s6.5 1.8 6.5 4.3V15c-1.9 1.1-4.1 1.6-6.5 1.6S7.4 16.1 5.5 15z' fill='#fff' stroke='#cfd8e0' stroke-width='.8'/>" +
            "<path d='M8.2 8.5c.9-.7 2.3-1.1 3.8-1.1s2.9.4 3.8 1.1v3.2c0 2.1-1.7 3.6-3.8 3.6s-3.8-1.5-3.8-3.6z' fill='#c9a37a'/>" +
            "<path d='M8.4 12.2c1 .7 2.2 1.1 3.6 1.1s2.6-.4 3.6-1.1c-.2 1.9-1.7 3.1-3.6 3.1s-3.4-1.2-3.6-3.1z' fill='#2b2b2b'/>" +
            "<ellipse cx='12' cy='4' rx='4.6' ry='1.5' fill='none' stroke='#141414' stroke-width='1.7'/></svg>",
        "non" =>   // businessman: dark suit, white collar, red tie
            "<svg viewBox='0 0 24 24' xmlns='http://www.w3.org/2000/svg'>" +
            "<circle cx='12' cy='7.5' r='3.6' fill='#e9b48f'/>" +
            "<path d='M8.3 6c.4-1.9 1.9-2.9 3.7-2.9S15.3 4.1 15.7 6c-1-.5-2.3-.8-3.7-.8s-2.7.3-3.7.8z' fill='#5a3a24'/>" +
            "<path d='M4.5 21.5v-1.3c0-2.5 1.9-4.1 4.4-4.7L12 20l3.1-4.5c2.5.6 4.4 2.2 4.4 4.7v1.3z' fill='#3f4b5b'/>" +
            "<path d='M9.5 15.8h5L12 19.4z' fill='#fff'/>" +
            "<path d='M11.3 16.1h1.4l.5 1.1-1.2 2.7-1.2-2.7z' fill='#d64525'/></svg>",
        _ =>   // total = mix: Saudi (ghutra, front) + non-Saudi businessman (behind)
            "<svg viewBox='0 0 24 24' xmlns='http://www.w3.org/2000/svg'>" +
            "<circle cx='16.3' cy='8.2' r='3' fill='#e9b48f'/>" +
            "<path d='M10.8 20v-.8c0-2.3 2.3-3.8 5.5-3.8s5.5 1.5 5.5 3.8v.8z' fill='#3f4b5b'/>" +
            "<path d='M14.7 15.7h3.2l-1.6 2.1z' fill='#fff'/>" +
            "<path d='M15.6 15.9h1.4l.4 1-1.1 2-1.1-2z' fill='#d64525'/>" +
            "<path d='M1.6 20.6v-1c0-2.4 2.4-4 5.6-4s5.6 1.6 5.6 4v1z' fill='#e9edf1'/>" +
            "<path d='M2 7.9C2 5.7 4.4 4.3 7.2 4.3S12.4 5.7 12.4 7.9v5.3c-1.5.8-3.2 1.2-5.2 1.2S3.5 14 2 13.2z' fill='#fff' stroke='#cfd8e0' stroke-width='.6'/>" +
            "<path d='M4.2 9.4c.9-.5 1.9-.8 3-.8s2.1.3 3 .8v2.1c0 1.6-1.4 2.7-3 2.7s-3-1.1-3-2.7z' fill='#c9a37a'/>" +
            "<ellipse cx='7.2' cy='5.9' rx='3.4' ry='1.1' fill='none' stroke='#141414' stroke-width='1.3'/></svg>"
    };

    /// <summary>A hover-tooltipped span wrapping the icon (tooltip text is HTML-encoded).</summary>
    public static string Html(string kind, string tip)
    {
        var t = System.Net.WebUtility.HtmlEncode(tip);
        return $"<span class='nat-ic' title='{t}' role='img' aria-label='{t}'>{Svg(kind)}</span>";
    }
}
