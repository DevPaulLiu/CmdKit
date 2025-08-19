using System.Drawing;

namespace CmdKit.Theme;

public enum AppTheme
{
    Dark,
    Light,
    Blossom // feminine friendly pink theme
}

public static class ThemeColors
{
    // Dark palette
    public static Color Background => Color.FromArgb(30, 33, 38);
    public static Color Surface => Color.FromArgb(42, 46, 52);
    public static Color SurfaceAlt => Color.FromArgb(52, 57, 64);
    public static Color SurfaceAccent => Color.FromArgb(0, 191, 165);
    public static Color Accent => Color.FromArgb(0, 191, 165);
    public static Color AccentHover => Color.FromArgb(0, 208, 180);
    public static Color AccentActive => Color.FromArgb(0, 171, 150);
    public static Color TextPrimary => Color.FromArgb(235, 238, 240);
    public static Color TextSecondary => Color.FromArgb(155, 165, 175);
    public static Color Border => Color.FromArgb(62, 68, 76);
    public static Color BorderStrong => Color.FromArgb(80, 86, 94);
    public static Color GridHeader => Color.FromArgb(48, 52, 58);
    public static Color GridRow => Color.FromArgb(38, 42, 48);
    public static Color GridRowAlt => Color.FromArgb(44, 48, 54);
    public static Color GridSelection => Color.FromArgb(0, 191, 165);
    public static Color Error => Color.FromArgb(229, 57, 53);
}

public static class ThemeColorsLight
{
    public static Color Background => Color.FromArgb(245, 247, 249);
    public static Color Surface => Color.FromArgb(255, 255, 255);
    public static Color SurfaceAlt => Color.FromArgb(240, 243, 246);
    public static Color SurfaceAccent => Color.FromArgb(0, 150, 136);
    public static Color Accent => Color.FromArgb(0, 150, 136);
    public static Color AccentHover => Color.FromArgb(0, 170, 154);
    public static Color AccentActive => Color.FromArgb(0, 128, 116);
    public static Color TextPrimary => Color.FromArgb(33, 37, 41);
    public static Color TextSecondary => Color.FromArgb(96, 105, 114);
    public static Color Border => Color.FromArgb(210, 215, 220);
    public static Color BorderStrong => Color.FromArgb(190, 195, 200);
    public static Color GridHeader => Color.FromArgb(233, 236, 239);
    public static Color GridRow => Color.FromArgb(255, 255, 255);
    public static Color GridRowAlt => Color.FromArgb(246, 248, 250);
    public static Color GridSelection => Color.FromArgb(0, 150, 136);
    public static Color Error => Color.FromArgb(211, 47, 47);
}

public static class ThemeColorsBlossom
{
    // Soft pink / blossom palette
    public static Color Background => Color.FromArgb(250, 242, 247);          // very light pink background
    public static Color Surface => Color.FromArgb(255, 250, 253);             // card surface
    public static Color SurfaceAlt => Color.FromArgb(245, 232, 240);          // hover surface
    public static Color SurfaceAccent => Color.FromArgb(231, 84, 128);        // accent fill
    public static Color Accent => Color.FromArgb(231, 84, 128);               // primary accent (rose)
    public static Color AccentHover => Color.FromArgb(240, 104, 146);         // hover accent
    public static Color AccentActive => Color.FromArgb(203, 61, 108);         // active accent
    public static Color TextPrimary => Color.FromArgb(60, 38, 51);            // deep plum text
    public static Color TextSecondary => Color.FromArgb(120, 85, 100);        // muted plum
    public static Color Border => Color.FromArgb(230, 210, 220);              // soft border
    public static Color BorderStrong => Color.FromArgb(210, 180, 195);
    public static Color GridHeader => Color.FromArgb(245, 230, 238);
    public static Color GridRow => Color.FromArgb(255, 250, 253);
    public static Color GridRowAlt => Color.FromArgb(249, 240, 246);
    public static Color GridSelection => Color.FromArgb(231, 84, 128);
    public static Color Error => Color.FromArgb(198, 40, 40);
}
