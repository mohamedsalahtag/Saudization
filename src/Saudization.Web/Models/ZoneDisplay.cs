namespace Saudization.Web.Models;

public static class ZoneDisplay
{
    public static string Css(NitaqatZone z) => z switch
    {
        NitaqatZone.Red => "zone-red",
        NitaqatZone.LowGreen => "zone-lowgreen",
        NitaqatZone.MediumGreen => "zone-medgreen",
        NitaqatZone.HighGreen => "zone-highgreen",
        NitaqatZone.Platinum => "zone-platinum",
        _ => "zone-unknown"
    };

    public static string CardCss(NitaqatZone z) => z switch
    {
        NitaqatZone.Red => "zc-red",
        NitaqatZone.LowGreen => "zc-lowgreen",
        NitaqatZone.MediumGreen => "zc-medgreen",
        NitaqatZone.HighGreen => "zc-highgreen",
        NitaqatZone.Platinum => "zc-platinum",
        _ => "zc-unknown"
    };

    /// <summary>Resource key in SharedResource for this zone's label.</summary>
    public static string Key(NitaqatZone z) => z switch
    {
        NitaqatZone.Red => "Zone_Red",
        NitaqatZone.LowGreen => "Zone_LowGreen",
        NitaqatZone.MediumGreen => "Zone_MediumGreen",
        NitaqatZone.HighGreen => "Zone_HighGreen",
        NitaqatZone.Platinum => "Zone_Platinum",
        _ => "Zone_Unknown"
    };

    /// <summary>Default zone hex, used when a Record has no per-zone override.</summary>
    public static string DefaultColor(NitaqatZone z) => z switch
    {
        NitaqatZone.Red => "#d64545",
        NitaqatZone.LowGreen => "#9bc26b",
        NitaqatZone.MediumGreen => "#5aa544",
        NitaqatZone.HighGreen => "#2f9e44",
        NitaqatZone.Platinum => "#5a7d9a",
        _ => "#94a3b8"
    };

    public static string StatusCss(CellStatus s) => s switch
    {
        CellStatus.Red => "cell-red",
        CellStatus.LightGreen => "cell-lightgreen",
        CellStatus.HighGreen => "cell-highgreen",
        CellStatus.Exempt => "cell-exempt",
        _ => ""
    };
}
