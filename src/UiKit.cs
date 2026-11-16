using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WifiList
{
    /// <summary>
    /// Makes the process DPI aware once, before any window exists, and exposes
    /// the resulting scale factor. Without this Windows treats the app as a
    /// bitmap and stretches it on high-DPI displays, which blurs every glyph.
    /// </summary>
    internal static class Dpi
    {
        [DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness(int value);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        /// <summary>device pixels per design pixel (1.0 at 100%, 2.0 at 200%).</summary>
        public static float Scale = 1F;

        public static void Enable()
        {
            // 1 = PROCESS_SYSTEM_DPI_AWARE: one scale for the session, which is
            // exactly what a fixed-pixel layout needs. Per-monitor awareness
            // would require re-laying out on every DPI change.
            try
            {
                SetProcessDpiAwareness(1);
            }
            catch
            {
                try { SetProcessDPIAware(); }
                catch { }
            }

            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    Scale = g.DpiX / 96F;
                }
            }
            catch
            {
                Scale = 1F;
            }

            if (Scale < 1F) Scale = 1F;
            if (Scale > 4F) Scale = 4F;
        }
    }

    internal enum UiTheme
    {
        Dark,
        Light
    }

    /// <summary>Glyphs available to <see cref="GlyphButton"/>.</summary>
    internal enum GlyphKind
    {
        Sun,
        Moon,
        Minimum,
        Close,
        Refresh,
        Globe,
        Info
    }

    /// <summary>
    /// Shared look and feel. The palette is mutable so the whole window can
    /// switch between the dark and the light theme at runtime; every painter
    /// reads these fields at paint time, so a theme change is just an
    /// Invalidate.
    /// </summary>
    internal static class Ui
    {
        public static UiTheme Theme;

        public static Color Bg, Panel, PanelHi, RowAlt, Line, LineHi;
        public static Color Fg, Dim, Mute, Accent, Accent2, AccentDeep, OnAccent;
        public static Color Ok, Info, Warn, Danger;
        public static Color SelBg, Hover, GlyphInk;

        // fonts. Rebuilt whenever the interface language changes, because every
        // script needs its own family: YaHei has no Thai, Segoe UI has no CJK,
        // Leelawadee UI has both Thai and Latin.
        public static Font FTitle, FSub, FCardLabel, FCardValue, FCardValueSm, FCardSub;
        public static Font FSsid, FBig, FRing, FMono, FVal, FKey, FBtn, FBtnBold;
        public static Font FGrid, FGridBold, FEmpty;

        /// <summary>Base UI family for the active language.</summary>
        private static string Family()
        {
            if (L.Current == UiLang.En) return "Segoe UI";
            if (L.Current == UiLang.Th) return "Leelawadee UI";
            return "Microsoft YaHei UI";
        }

        private static Font Nf(float size, FontStyle style)
        {
            try
            {
                return new Font(Family(), size, style, GraphicsUnit.Point);
            }
            catch
            {
                return new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Point);
            }
        }

        /// <summary>(Re)creates the font set. Old instances are dropped on purpose:
        /// controls that still hold them are rebuilt by the caller.</summary>
        public static void LoadFonts()
        {
            FTitle = Nf(12.5F, FontStyle.Bold);
            FSub = Nf(8.75F, FontStyle.Regular);
            FCardLabel = Nf(8.25F, FontStyle.Regular);
            FCardValue = Nf(16.5F, FontStyle.Bold);
            FCardValueSm = Nf(11.25F, FontStyle.Bold);
            FCardSub = Nf(8F, FontStyle.Regular);
            FSsid = Nf(11.25F, FontStyle.Bold);
            FBig = Nf(17.5F, FontStyle.Bold);
            FRing = Nf(12F, FontStyle.Bold);
            FKey = Nf(7.75F, FontStyle.Regular);
            FBtn = Nf(8.75F, FontStyle.Regular);
            FBtnBold = Nf(8.75F, FontStyle.Bold);
            FGrid = Nf(9F, FontStyle.Regular);
            FGridBold = Nf(9F, FontStyle.Bold);
            FEmpty = Nf(8.75F, FontStyle.Regular);

            // monospaced families cover Latin and digits only; SSIDs and MAC
            // addresses are rendered with the proportional fonts instead
            FMono = new Font("Consolas", 8.25F);
            FVal = new Font("Consolas", 8.75F);
        }

        public const TextFormatFlags LeftTop =
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding |
            TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;

        public const TextFormatFlags LeftMid =
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
            TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;

        public const TextFormatFlags CenterTop =
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding |
            TextFormatFlags.SingleLine;

        public const string Version = "1.6.6";

        // ---- DPI helpers -------------------------------------------------
        // Every layout and paint constant is written in 96 dpi "design" units
        // and converted here, so the window keeps its proportions at 125%,
        // 150%, 200% and so on.
        // ------------------------------------------------------------------

        public static int Z(int design)
        {
            return (int)Math.Round(design * (double)Dpi.Scale);
        }

        public static float ZF(float design)
        {
            return design * Dpi.Scale;
        }

        /// <summary>Sets a control's bounds from design units.</summary>
        public static void B(Control c, int x, int y, int w, int h)
        {
            if (c == null) return;
            c.SetBounds(Z(x), Z(y), Z(w), Z(h));
        }

        static Ui()
        {
            LoadFonts();
            Load(UiTheme.Dark);
        }

        private static void Load(UiTheme t)
        {
            Theme = t;

            if (t == UiTheme.Light)
            {
                Bg = Color.FromArgb(0xF3, 0xF5, 0xF9);
                Panel = Color.FromArgb(0xFF, 0xFF, 0xFF);
                PanelHi = Color.FromArgb(0xEC, 0xF1, 0xF7);
                RowAlt = Color.FromArgb(0xF8, 0xFA, 0xFC);
                Line = Color.FromArgb(0xDD, 0xE4, 0xEE);
                LineHi = Color.FromArgb(0xC6, 0xD3, 0xE2);
                Fg = Color.FromArgb(0x15, 0x20, 0x2C);
                Dim = Color.FromArgb(0x44, 0x56, 0x6A);
                Mute = Color.FromArgb(0x70, 0x82, 0x97);
                Accent = Color.FromArgb(0x0E, 0xA5, 0xE9);
                Accent2 = Color.FromArgb(0x08, 0x91, 0xB2);
                AccentDeep = Color.FromArgb(0x02, 0x84, 0xC7);
                OnAccent = Color.FromArgb(0xFF, 0xFF, 0xFF);
                Ok = Color.FromArgb(0x04, 0x96, 0x66);
                Info = Color.FromArgb(0x25, 0x63, 0xEB);
                Warn = Color.FromArgb(0xD9, 0x77, 0x06);
                Danger = Color.FromArgb(0xDC, 0x26, 0x26);
                SelBg = Color.FromArgb(0xDB, 0xEA, 0xFE);
                Hover = Color.FromArgb(0xE2, 0xE9, 0xF2);
                GlyphInk = Color.FromArgb(0xFF, 0xFF, 0xFF);
            }
            else
            {
                Bg = Color.FromArgb(0x0A, 0x0F, 0x16);
                Panel = Color.FromArgb(0x12, 0x1B, 0x26);
                PanelHi = Color.FromArgb(0x1B, 0x27, 0x36);
                RowAlt = Color.FromArgb(0x0F, 0x17, 0x21);
                Line = Color.FromArgb(0x24, 0x32, 0x42);
                LineHi = Color.FromArgb(0x33, 0x45, 0x58);
                Fg = Color.FromArgb(0xE8, 0xEE, 0xF6);
                Dim = Color.FromArgb(0x99, 0xAB, 0xBE);
                Mute = Color.FromArgb(0x6D, 0x81, 0x96);
                Accent = Color.FromArgb(0x38, 0xBD, 0xF8);
                Accent2 = Color.FromArgb(0x22, 0xD3, 0xEE);
                AccentDeep = Color.FromArgb(0x0E, 0xA5, 0xE9);
                OnAccent = Color.FromArgb(0x04, 0x14, 0x1E);
                Ok = Color.FromArgb(0x34, 0xD3, 0x99);
                Info = Color.FromArgb(0x60, 0xA5, 0xFA);
                Warn = Color.FromArgb(0xFB, 0xBF, 0x24);
                Danger = Color.FromArgb(0xFB, 0x71, 0x85);
                SelBg = Color.FromArgb(0x1C, 0x38, 0x4C);
                Hover = Color.FromArgb(0x26, 0x38, 0x4A);
                GlyphInk = Color.FromArgb(0x04, 0x14, 0x1E);
            }
        }

        public static void SetTheme(UiTheme t)
        {
            Load(t);
        }

        // ---- persisted preference ----

        private static string ThemeFile()
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WifiList");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return Path.Combine(dir, "theme.cfg");
            }
            catch
            {
                return null;
            }
        }

        public static UiTheme SavedTheme()
        {
            try
            {
                string f = ThemeFile();
                if (f != null && File.Exists(f))
                {
                    string s = File.ReadAllText(f).Trim().ToLowerInvariant();
                    if (s == "light") return UiTheme.Light;
                }
            }
            catch { }
            return UiTheme.Dark;
        }

        public static void SaveTheme()
        {
            try
            {
                string f = ThemeFile();
                if (f != null) File.WriteAllText(f, Theme == UiTheme.Light ? "light" : "dark");
            }
            catch { }
        }

        // ---- drawing helpers ----

        public static Color Mix(Color a, Color b, double t)
        {
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            return Color.FromArgb(
                (int)Math.Round(a.A + (b.A - a.A) * t),
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (r.Width <= 0 || r.Height <= 0)
            {
                path.AddRectangle(new Rectangle(r.X, r.Y, Math.Max(1, r.Width), Math.Max(1, r.Height)));
                return path;
            }
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            if (d <= 1)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRound(Graphics g, Rectangle r, int radius, Color fill)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Round(r, radius))
            {
                using (SolidBrush b = new SolidBrush(fill))
                {
                    g.FillPath(b, p);
                }
            }
            g.SmoothingMode = old;
        }

        public static void FillRound(Graphics g, Rectangle r, int radius, Color fill, Color border)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Round(r, radius))
            {
                using (SolidBrush b = new SolidBrush(fill))
                {
                    g.FillPath(b, p);
                }
                if (border.A > 0)
                {
                    using (Pen pen = new Pen(border, 1F))
                    {
                        g.DrawPath(pen, p);
                    }
                }
            }
            g.SmoothingMode = old;
        }

        public static void FillRoundGradient(Graphics g, Rectangle r, int radius, Color top, Color bottom)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Round(r, radius))
            {
                using (LinearGradientBrush b = new LinearGradientBrush(
                    new Rectangle(r.X, r.Y, r.Width, r.Height + 1), top, bottom, LinearGradientMode.Vertical))
                {
                    g.FillPath(b, p);
                }
            }
            g.SmoothingMode = old;
        }

        /// <summary>Pixel width of a single line of text in the active language.</summary>
        public static int TextW(string text, Font font)
        {
            if (text == null || text.Length == 0) return 0;
            return TextRenderer.MeasureText(text, font, new Size(4096, 4096),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
        }

        public static void Text(Graphics g, string text, Font font, Color color, Rectangle bounds, TextFormatFlags flags)
        {
            TextRenderer.DrawText(g, text == null ? "" : text, font, bounds, color, flags);
        }

        /// <summary>Vector app mark: rounded gradient tile with a wifi glyph.</summary>
        public static void DrawLogo(Graphics g, Rectangle r)
        {
            if (r.Width < 10 || r.Height < 10) return;

            FillRoundGradient(g, r, (int)Math.Round(r.Width * 0.26), Accent, AccentDeep);

            float s = r.Width;
            float cx = r.X + s * 0.5F;
            float cy = r.Y + s * 0.80F;
            float thick = s * 0.072F;
            float[] radii = new float[] { s * 0.155F, s * 0.300F, s * 0.445F };

            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen pen = new Pen(GlyphInk, thick))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                for (int i = 0; i < radii.Length; i++)
                {
                    RectangleF arc = new RectangleF(cx - radii[i], cy - radii[i], radii[i] * 2F, radii[i] * 2F);
                    g.DrawArc(pen, arc, 206F, 128F);
                }
            }
            using (SolidBrush brush = new SolidBrush(GlyphInk))
            {
                float d = s * 0.155F;
                g.FillEllipse(brush, cx - d / 2F, cy - d / 2F + s * 0.015F, d, d);
            }
            g.SmoothingMode = old;
        }

        public static Color TierColor(string tier)
        {
            if (tier == "wpa3") return Ok;
            if (tier == "wpa2") return Info;
            if (tier == "wep") return Danger;
            if (tier == "open") return Mute;
            return Warn;
        }

        /// <summary>Applies the palette to a context menu, including its renderer.</summary>
        public static void StyleMenu(ContextMenuStrip m)
        {
            if (m == null) return;
            m.BackColor = PanelHi;
            m.ForeColor = Fg;
            m.Font = FSub;
            m.ShowImageMargin = false;
            m.ShowCheckMargin = false;
            m.Renderer = MenuRenderer();
            m.Padding = new Padding(0, Z(4), 0, Z(4));
        }

        private static UiMenuRenderer _menuRenderer;

        /// <summary>One renderer instance shared by the menu and every submenu.</summary>
        public static UiMenuRenderer MenuRenderer()
        {
            if (_menuRenderer == null) _menuRenderer = new UiMenuRenderer();
            return _menuRenderer;
        }

        /// <summary>
        /// Builds a menu item with a little vertical air. Native items are only
        /// as tall as their text, which is what made the grid context menu look
        /// cramped.
        /// </summary>
        public static ToolStripMenuItem MenuItem(string text)
        {
            ToolStripMenuItem mi = new ToolStripMenuItem(text);
            mi.ForeColor = Fg;
            mi.Padding = new Padding(0, Z(3), 0, Z(3));
            return mi;
        }

        public static ToolStripSeparator MenuSeparator()
        {
            ToolStripSeparator sep = new ToolStripSeparator();
            sep.Margin = new Padding(0, Z(3), 0, Z(3));
            return sep;
        }

        public static void StyleButton(Button b, bool primary)
        {            if (b == null) return;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.UseVisualStyleBackColor = false;
            b.Font = primary ? FBtnBold : FBtn;
            b.Cursor = Cursors.Hand;
            b.TabStop = false;
            b.AutoSize = false;
            if (primary)
            {
                b.BackColor = Accent;
                b.ForeColor = OnAccent;
                b.FlatAppearance.MouseOverBackColor = Mix(Accent, Color.White, 0.15);
                b.FlatAppearance.MouseDownBackColor = AccentDeep;
            }
            else
            {
                b.BackColor = PanelHi;
                b.ForeColor = Dim;
                b.FlatAppearance.MouseOverBackColor = Hover;
                b.FlatAppearance.MouseDownBackColor = Mix(PanelHi, Fg, 0.10);
            }
        }

        public static Button MakeButton(string text, bool primary)
        {
            Button b = new Button();
            b.Text = text;
            StyleButton(b, primary);
            return b;
        }
    }

    /// <summary>Owner drawn icon button used by the custom caption bar.</summary>
    internal sealed class GlyphButton : Control
    {
        private bool _hover;
        private bool _press;

        public GlyphKind Kind;
        public bool DangerHover;

        public GlyphButton(GlyphKind kind)
        {
            Kind = kind;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                          ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            this.BackColor = Color.Transparent;
            this.Cursor = Cursors.Hand;
            this.TabStop = false;
            this.Size = new Size(30, 26);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _press = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _press = true;
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _press = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int p = Ui.Z(1);
            Rectangle r = new Rectangle(p, p, this.Width - p * 2 - Ui.Z(1), this.Height - p * 2 - Ui.Z(1));
            if (r.Width > 0 && r.Height > 0)
            {
                if (_press)
                {
                    Ui.FillRound(g, r, Ui.Z(6), DangerHover ? Ui.Danger : Ui.Line);
                }
                else if (_hover)
                {
                    Ui.FillRound(g, r, Ui.Z(6), DangerHover ? Ui.Mix(Ui.Danger, Ui.Panel, 0.32) : Ui.Hover);
                }
            }

            Color ink = (DangerHover && (_hover || _press)) ? Color.White : Ui.Dim;
            PaintGlyph(g, ink);
        }

        private void PaintGlyph(Graphics g, Color ink)
        {
            float cx = this.Width / 2F;
            float cy = this.Height / 2F + Ui.ZF(0.5F);
            float k = Ui.ZF(1F);

            if (Kind == GlyphKind.Moon)
            {
                float r = 7F * k;
                GraphicsPath outer = new GraphicsPath();
                GraphicsPath inner = new GraphicsPath();
                try
                {
                    outer.AddEllipse(cx - r, cy - r, r * 2F, r * 2F);
                    inner.AddEllipse(cx - r + r * 0.82F, cy - r - r * 0.22F, r * 2F, r * 2F);
                    using (Region reg = new Region(outer))
                    {
                        reg.Exclude(inner);
                        using (SolidBrush b = new SolidBrush(ink))
                        {
                            g.FillRegion(b, reg);
                        }
                    }
                }
                finally
                {
                    outer.Dispose();
                    inner.Dispose();
                }
                return;
            }

            using (Pen pen = new Pen(ink, 1.4F * k))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;

                if (Kind == GlyphKind.Minimum)
                {
                    g.DrawLine(pen, cx - 5F * k, cy, cx + 5F * k, cy);
                }
                else if (Kind == GlyphKind.Close)
                {
                    g.DrawLine(pen, cx - 4.5F * k, cy - 4.5F * k, cx + 4.5F * k, cy + 4.5F * k);
                    g.DrawLine(pen, cx + 4.5F * k, cy - 4.5F * k, cx - 4.5F * k, cy + 4.5F * k);
                }
                else if (Kind == GlyphKind.Sun)
                {
                    g.DrawEllipse(pen, cx - 3.4F * k, cy - 3.4F * k, 6.8F * k, 6.8F * k);
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i * Math.PI / 4.0;
                        float x1 = cx + (float)(Math.Cos(a) * 5.6) * k;
                        float y1 = cy + (float)(Math.Sin(a) * 5.6) * k;
                        float x2 = cx + (float)(Math.Cos(a) * 7.4) * k;
                        float y2 = cy + (float)(Math.Sin(a) * 7.4) * k;
                        g.DrawLine(pen, x1, y1, x2, y2);
                    }
                }
                else if (Kind == GlyphKind.Refresh)
                {
                    RectangleF arc = new RectangleF(cx - 5.2F * k, cy - 5.2F * k, 10.4F * k, 10.4F * k);
                    g.DrawArc(pen, arc, 60F, 265F);

                    // arrow head sitting on the open end of the arc
                    double a = 60.0 * Math.PI / 180.0;
                    float px = cx + (float)(Math.Cos(a) * 5.2) * k;
                    float py = cy + (float)(Math.Sin(a) * 5.2) * k;
                    float nx = (float)Math.Cos(a);
                    float ny = (float)Math.Sin(a);
                    float tx = (float)Math.Sin(a);
                    float ty = -(float)Math.Cos(a);
                    float half = 2.6F * k;
                    PointF[] tri = new PointF[]
                    {
                        new PointF(px + nx * half, py + ny * half),
                        new PointF(px - nx * half, py - ny * half),
                        new PointF(px + tx * 3.6F * k, py + ty * 3.6F * k)
                    };
                    using (SolidBrush b = new SolidBrush(ink))
                    {
                        g.FillPolygon(b, tri);
                    }
                }
                else if (Kind == GlyphKind.Globe)
                {
                    g.DrawEllipse(pen, cx - 5.4F * k, cy - 5.4F * k, 10.8F * k, 10.8F * k);
                    g.DrawLine(pen, cx - 5.4F * k, cy, cx + 5.4F * k, cy);
                    g.DrawEllipse(pen, cx - 2.5F * k, cy - 5.4F * k, 5F * k, 10.8F * k);
                }
                else if (Kind == GlyphKind.Info)
                {
                    g.DrawEllipse(pen, cx - 5.4F * k, cy - 5.4F * k, 10.8F * k, 10.8F * k);
                    g.DrawLine(pen, cx, cy - 0.5F * k, cx, cy + 3.6F * k);
                    using (SolidBrush dot = new SolidBrush(ink))
                    {
                        g.FillEllipse(dot, cx - 0.85F * k, cy - 3.9F * k, 1.7F * k, 1.7F * k);
                    }
                }
            }
        }
    }

    /// <summary>Rounded filter chip with a check box, used instead of a native CheckBox.</summary>
    internal sealed class ChipToggle : Control
    {
        private bool _on;
        private bool _hover;

        public event EventHandler Toggled;

        public ChipToggle()
        {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                          ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            this.BackColor = Color.Transparent;
            this.Cursor = Cursors.Hand;
            this.TabStop = false;
            this.Size = new Size(108, 30);
        }

        public bool On
        {
            get { return _on; }
            set
            {
                if (_on == value) return;
                _on = value;
                Invalidate();
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnClick(EventArgs e)
        {
            _on = !_on;
            Invalidate();
            if (Toggled != null) Toggled(this, EventArgs.Empty);
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle r = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            Color fill = _on ? Ui.Mix(Ui.Accent, Ui.Panel, 0.85) : Ui.Panel;
            Color border = _on ? Ui.Mix(Ui.Accent, Ui.Panel, 0.42) : (_hover ? Ui.LineHi : Ui.Line);
            Ui.FillRound(g, r, Ui.Z(8), fill, border);

            int bs = Ui.Z(13);
            int boxX = Ui.Z(11);
            int boxY = (this.Height - bs) / 2;
            Rectangle box = new Rectangle(boxX, boxY, bs, bs);
            if (_on)
            {
                Ui.FillRound(g, box, Ui.Z(4), Ui.Accent);
                float k = Ui.ZF(1F);
                using (Pen pen = new Pen(Ui.OnAccent, 1.6F * k))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    g.DrawLines(pen, new PointF[]
                    {
                        new PointF(box.X + 3.2F * k, box.Y + 6.8F * k),
                        new PointF(box.X + 5.6F * k, box.Y + 9.2F * k),
                        new PointF(box.X + 9.8F * k, box.Y + 3.9F * k)
                    });
                }
            }
            else
            {
                Ui.FillRound(g, box, Ui.Z(4), Ui.Bg, _hover ? Ui.LineHi : Ui.Line);
            }

            Ui.Text(g, this.Text, Ui.FSub, _on ? Ui.Fg : Ui.Dim,
                new Rectangle(Ui.Z(30), 0, Math.Max(1, this.Width - Ui.Z(34)), this.Height), Ui.LeftMid);
        }
    }

    /// <summary>
    /// Context menus are drawn by ToolStrip's own renderer, which uses system
    /// colours; on a dark palette that gives near-white text on a light blue
    /// selection. This table keeps them on the app palette instead.
    /// </summary>
    internal sealed class UiMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Ui.PanelHi; } }
        public override Color MenuBorder { get { return Ui.LineHi; } }
        public override Color MenuItemBorder { get { return Ui.Hover; } }
        public override Color MenuItemSelected { get { return Ui.Hover; } }
        public override Color MenuItemSelectedGradientBegin { get { return Ui.Hover; } }
        public override Color MenuItemSelectedGradientEnd { get { return Ui.Hover; } }
        public override Color ImageMarginGradientBegin { get { return Ui.PanelHi; } }
        public override Color ImageMarginGradientMiddle { get { return Ui.PanelHi; } }
        public override Color ImageMarginGradientEnd { get { return Ui.PanelHi; } }
        public override Color SeparatorDark { get { return Ui.Line; } }
        public override Color SeparatorLight { get { return Ui.Line; } }
    }

    internal sealed class UiMenuRenderer : ToolStripProfessionalRenderer
    {
        public UiMenuRenderer()
            : base(new UiMenuColors())
        {
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Ui.Fg : Ui.Mute;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Ui.Dim;
            base.OnRenderArrow(e);
        }
    }
}
