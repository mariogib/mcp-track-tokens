using Microsoft.Win32;

namespace McpTrackTokens.Tray;

/// <summary>
/// Context menu that follows the Windows apps light/dark theme (and high contrast).
/// Re-applies on each open so theme flips take effect without restart.
/// </summary>
internal sealed class ThemedContextMenuStrip : ContextMenuStrip
{
    public ThemedContextMenuStrip()
    {
        Font = SystemFonts.MenuFont;
        ShowImageMargin = false;
        RenderMode = ToolStripRenderMode.Professional;
        Opening += (_, _) => ApplyTheme();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        ApplyTheme();
    }

    public void RefreshTheme() => ApplyTheme();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        }

        base.Dispose(disposing);
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle))
        {
            return;
        }

        if (IsHandleCreated)
        {
            BeginInvoke(ApplyTheme);
        }
        else
        {
            ApplyTheme();
        }
    }

    private void ApplyTheme()
    {
        if (SystemInformation.HighContrast)
        {
            Renderer = new ToolStripSystemRenderer();
            BackColor = SystemColors.Menu;
            ForeColor = SystemColors.MenuText;
            ApplyItemColors(SystemColors.MenuText);
            return;
        }

        if (IsAppsLightTheme())
        {
            var table = new LightColorTable();
            Renderer = new FlatThemeRenderer(table);
            BackColor = table.ToolStripDropDownBackground;
            ForeColor = table.MenuItemText;
            ApplyItemColors(table.MenuItemText);
            return;
        }

        var dark = new DarkColorTable();
        Renderer = new FlatThemeRenderer(dark);
        BackColor = dark.ToolStripDropDownBackground;
        ForeColor = dark.MenuItemText;
        ApplyItemColors(dark.MenuItemText);
    }

    private void ApplyItemColors(Color foreColor)
    {
        foreach (ToolStripItem item in Items)
        {
            item.BackColor = Color.Empty;
            item.ForeColor = foreColor;
        }
    }

    /// <summary>
    /// True when Windows "App mode" is light. Defaults to light only if the value is missing.
    /// </summary>
    internal static bool IsAppsLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value switch
            {
                int i => i != 0,
                long l => l != 0,
                uint u => u != 0,
                _ => true,
            };
        }
        catch
        {
            return true;
        }
    }

    private sealed class FlatThemeRenderer : ToolStripProfessionalRenderer
    {
        private readonly ThemeColorTable _table;

        public FlatThemeRenderer(ThemeColorTable table)
            : base(table)
        {
            _table = table;
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(_table.ToolStripDropDownBackground);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(_table.MenuBorder);
            var r = e.AffectedBounds;
            e.Graphics.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var bounds = new Rectangle(Point.Empty, e.Item.Size);
            var fill = e.Item.Selected && e.Item.Enabled
                ? _table.MenuItemSelected
                : _table.ToolStripDropDownBackground;
            using var brush = new SolidBrush(fill);
            e.Graphics.FillRectangle(brush, bounds);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (!e.Item.Enabled)
            {
                e.TextColor = _table.DisabledText;
            }
            else if (e.Item.Selected)
            {
                e.TextColor = _table.MenuItemText;
            }
            else
            {
                e.TextColor = _table.MenuItemText;
            }

            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var y = e.Item.Height / 2;
            using var pen = new Pen(_table.SeparatorDark);
            e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
        }
    }

    private abstract class ThemeColorTable : ProfessionalColorTable
    {
        public abstract Color MenuItemText { get; }
        public abstract Color DisabledText { get; }
    }

    private sealed class DarkColorTable : ThemeColorTable
    {
        // Win11 dark flyout-ish palette
        private static readonly Color Background = Color.FromArgb(32, 32, 32);
        private static readonly Color Border = Color.FromArgb(64, 64, 64);
        private static readonly Color Highlight = Color.FromArgb(60, 60, 60);
        private static readonly Color Separator = Color.FromArgb(70, 70, 70);

        public override Color MenuItemText => Color.FromArgb(255, 255, 255);
        public override Color DisabledText => Color.FromArgb(140, 140, 140);
        public override Color ToolStripDropDownBackground => Background;
        public override Color ImageMarginGradientBegin => Background;
        public override Color ImageMarginGradientMiddle => Background;
        public override Color ImageMarginGradientEnd => Background;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Highlight;
        public override Color MenuItemSelected => Highlight;
        public override Color MenuItemSelectedGradientBegin => Highlight;
        public override Color MenuItemSelectedGradientEnd => Highlight;
        public override Color MenuItemPressedGradientBegin => Highlight;
        public override Color MenuItemPressedGradientEnd => Highlight;
        public override Color SeparatorDark => Separator;
        public override Color SeparatorLight => Separator;
    }

    private sealed class LightColorTable : ThemeColorTable
    {
        private static readonly Color Background = Color.FromArgb(249, 249, 249);
        private static readonly Color Border = Color.FromArgb(204, 204, 204);
        private static readonly Color Highlight = Color.FromArgb(230, 230, 230);
        private static readonly Color Separator = Color.FromArgb(218, 218, 218);

        public override Color MenuItemText => Color.FromArgb(20, 20, 20);
        public override Color DisabledText => Color.FromArgb(150, 150, 150);
        public override Color ToolStripDropDownBackground => Background;
        public override Color ImageMarginGradientBegin => Background;
        public override Color ImageMarginGradientMiddle => Background;
        public override Color ImageMarginGradientEnd => Background;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Highlight;
        public override Color MenuItemSelected => Highlight;
        public override Color MenuItemSelectedGradientBegin => Highlight;
        public override Color MenuItemSelectedGradientEnd => Highlight;
        public override Color MenuItemPressedGradientBegin => Highlight;
        public override Color MenuItemPressedGradientEnd => Highlight;
        public override Color SeparatorDark => Separator;
        public override Color SeparatorLight => Separator;
    }
}
