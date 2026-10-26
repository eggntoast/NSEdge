using System.Drawing;
using System.Windows.Forms;
using Forms = System.Windows.Forms;

namespace NSEdge.App.Services;

// Retain native tray behavior while using the same dark surfaces as PrecisionDark.
internal static class TrayMenuTheme
{
    private static readonly Color Surface = ColorTranslator.FromHtml("#182232");
    private static readonly Color Raised = ColorTranslator.FromHtml("#26374B");
    private static readonly Color Border = ColorTranslator.FromHtml("#35465B");
    private static readonly Color Text = ColorTranslator.FromHtml("#F2F6FC");

    public static void Apply(Forms.ContextMenuStrip menu)
    {
        if (Forms.SystemInformation.HighContrast)
            return;

        menu.Renderer = new ToolStripProfessionalRenderer(new ColorTable()) { RoundedEdges = false };
        menu.BackColor = Surface;
        menu.ForeColor = Text;
        menu.ShowImageMargin = false;
        menu.ShowCheckMargin = false;
        menu.Padding = new Forms.Padding(5);
        menu.Font = new Font("Segoe UI", 9F);
        foreach (ToolStripItem item in menu.Items)
        {
            item.BackColor = Surface;
            item.ForeColor = Text;
            if (item is ToolStripMenuItem)
                item.Padding = new Forms.Padding(6, 5, 8, 5);
        }
    }

    private sealed class ColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Surface;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Border;
        public override Color MenuItemSelected => Raised;
        public override Color MenuItemSelectedGradientBegin => Raised;
        public override Color MenuItemSelectedGradientEnd => Raised;
        public override Color MenuItemPressedGradientBegin => Raised;
        public override Color MenuItemPressedGradientMiddle => Raised;
        public override Color MenuItemPressedGradientEnd => Raised;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Border;
        public override Color CheckBackground => Raised;
        public override Color CheckSelectedBackground => Raised;
        public override Color CheckPressedBackground => Raised;
    }
}
