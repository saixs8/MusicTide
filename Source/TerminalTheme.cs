namespace SpectrumKlinePlayer;

internal static class TerminalTheme
{
    public static readonly Color Background = Color.FromArgb(27, 29, 33);
    public static readonly Color Panel = Color.FromArgb(32, 34, 39);
    public static readonly Color Toolbar = Color.FromArgb(43, 45, 50);
    public static readonly Color Input = Color.FromArgb(37, 40, 46);
    public static readonly Color Border = Color.FromArgb(61, 64, 71);
    public static readonly Color Grid = Color.FromArgb(46, 49, 55);
    public static readonly Color Text = Color.FromArgb(210, 215, 224);
    public static readonly Color Muted = Color.FromArgb(133, 143, 158);
    public static readonly Color Cyan = Color.FromArgb(0, 188, 207);
    public static readonly Color Rise = Color.FromArgb(244, 68, 79);
    public static readonly Color Fall = Color.FromArgb(36, 200, 109);
    public static readonly Color Yellow = Color.FromArgb(231, 190, 65);
    public static readonly Color Selected = Color.FromArgb(35, 65, 81);
    public static readonly Color MenuText = Color.FromArgb(244, 248, 255);
    public static void SizeMenu(ToolStrip strip, float size, bool top = false)
    {
        strip.Font = new Font("Microsoft YaHei UI", size, top ? FontStyle.Bold : FontStyle.Regular);
        strip.Renderer = new TerminalMenuRenderer();
        foreach (var item in strip.Items.OfType<ToolStripMenuItem>())
        {
            if(top)item.Overflow=ToolStripItemOverflow.AsNeeded;
            item.Font = strip.Font; item.Padding = top ? new Padding(6, 2, 6, 2) : new Padding(6, 3, 6, 3);
            if (item.HasDropDownItems) SizeMenu(item.DropDown, size);
        }
    }
}

internal sealed class TerminalMenuRenderer : ToolStripProfessionalRenderer
{
    public TerminalMenuRenderer() : base(new TerminalColors()) { RoundedEdges = false; }
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor=e.Item?.Enabled==true?TerminalTheme.Cyan:TerminalTheme.Muted;
        base.OnRenderArrow(e);
    }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = !e.Item.Enabled ? TerminalTheme.Muted
            : e.Item.Selected || e.Item.Pressed ? Color.FromArgb(112, 232, 255)
            : TerminalTheme.MenuText;
        base.OnRenderItemText(e);
    }

    private sealed class TerminalColors : ProfessionalColorTable
    {
        public override Color MenuStripGradientBegin => TerminalTheme.Toolbar;
        public override Color MenuStripGradientEnd => TerminalTheme.Toolbar;
        public override Color ToolStripDropDownBackground => TerminalTheme.Panel;
        public override Color ImageMarginGradientBegin => TerminalTheme.Panel;
        public override Color ImageMarginGradientMiddle => TerminalTheme.Panel;
        public override Color ImageMarginGradientEnd => TerminalTheme.Panel;
        public override Color MenuItemSelected => TerminalTheme.Selected;
        public override Color MenuItemSelectedGradientBegin => TerminalTheme.Selected;
        public override Color MenuItemSelectedGradientEnd => TerminalTheme.Selected;
        public override Color MenuItemPressedGradientBegin => TerminalTheme.Selected;
        public override Color MenuItemPressedGradientMiddle => TerminalTheme.Selected;
        public override Color MenuItemPressedGradientEnd => TerminalTheme.Selected;
        public override Color MenuItemBorder => TerminalTheme.Cyan;
        public override Color MenuBorder => TerminalTheme.Border;
        public override Color SeparatorDark => TerminalTheme.Border;
        public override Color SeparatorLight => TerminalTheme.Border;
    }
}
