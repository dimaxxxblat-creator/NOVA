using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;
using Nova.Core;
namespace Nova.App.Services;
public static class TrayAppearance {
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    public static Color Accent(AccentTheme theme) => theme switch { AccentTheme.Blue=>Color.FromArgb(103,189,247), AccentTheme.Purple=>Color.FromArgb(186,150,255), AccentTheme.Mint=>Color.FromArgb(101,217,177), _=>Color.FromArgb(255,115,17) };
    public static Icon MakeIcon(AccentTheme theme) {
        using var bmp=new Bitmap(32,32); using var graphics=Graphics.FromImage(bmp); graphics.SmoothingMode=SmoothingMode.AntiAlias; graphics.Clear(Color.Transparent);
        using var dark=new SolidBrush(Color.FromArgb(25,24,28)); using var accent=new Pen(Accent(theme),3.4f) { StartCap=LineCap.Round, EndCap=LineCap.Round, LineJoin=LineJoin.Round };
        graphics.FillEllipse(dark,1,1,30,30); graphics.DrawLines(accent,new[]{new PointF(8,23),new PointF(8,9),new PointF(24,23),new PointF(24,9)});
        var handle=bmp.GetHicon(); try { using var temp=Icon.FromHandle(handle); return (Icon)temp.Clone(); } finally { DestroyIcon(handle); }
    }
    public static Forms.ToolStripRenderer Renderer() => new Forms.ToolStripProfessionalRenderer(new Palette()) { RoundedEdges=false };
    sealed class Palette: Forms.ProfessionalColorTable {
        static readonly Color Bg=Color.FromArgb(30,30,34), Hover=Color.FromArgb(67,51,41), Border=Color.FromArgb(75,72,78);
        public override Color ToolStripDropDownBackground=>Bg; public override Color ImageMarginGradientBegin=>Bg; public override Color ImageMarginGradientMiddle=>Bg; public override Color ImageMarginGradientEnd=>Bg;
        public override Color MenuBorder=>Border; public override Color MenuItemBorder=>Hover; public override Color MenuItemSelected=>Hover; public override Color MenuItemSelectedGradientBegin=>Hover; public override Color MenuItemSelectedGradientEnd=>Hover;
        public override Color MenuItemPressedGradientBegin=>Hover; public override Color MenuItemPressedGradientMiddle=>Hover; public override Color MenuItemPressedGradientEnd=>Hover; public override Color SeparatorDark=>Border; public override Color SeparatorLight=>Bg;
    }
}
