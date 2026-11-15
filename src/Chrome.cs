using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WifiList
{
    /// <summary>
    /// Borderless window shell: no system title bar, but native drag, native
    /// edge/corner resizing, Windows 11 rounded corners and a themed caption
    /// band that subclasses paint themselves.
    /// </summary>
    internal class ChromeForm : Form
    {
        private const int WM_NCHITTEST = 0x0084;
        private const int WM_GETMINMAXINFO = 0x0024;

        /// <summary>Caption height in design (96 dpi) units.</summary>
        protected int CaptionHeight = 48;

        protected bool AllowResize = true;

        private static Icon _appIcon;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        /// <summary>Caption height in device pixels.</summary>
        protected int CapH
        {
            get { return Ui.Z(CaptionHeight); }
        }

        private int Edge
        {
            get { return Ui.Z(6); }
        }

        public ChromeForm()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.BackColor = Ui.Bg;
            this.ForeColor = Ui.Fg;
            this.Font = Ui.FSub;
            this.AutoScaleMode = AutoScaleMode.None;
            this.KeyPreview = true;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer |
                          ControlStyles.AllPaintingInWmPaint, true);

            // The taskbar and Alt+Tab take their picture from the window icon.
            // /win32icon only changes the icon Explorer shows for the .exe.
            try
            {
                Icon ic = AppIcon();
                if (ic != null) this.Icon = ic;
            }
            catch { }
        }

        /// <summary>Icon embedded as a managed resource, with an exe fallback.</summary>
        protected static Icon AppIcon()
        {
            if (_appIcon == null)
            {
                try
                {
                    Stream s = typeof(ChromeForm).Assembly.GetManifestResourceStream("WifiList.AppIcon.ico");
                    if (s != null)
                    {
                        using (s)
                        {
                            _appIcon = new Icon(s);
                        }
                    }
                }
                catch { }

                if (_appIcon == null)
                {
                    try { _appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
                    catch { }
                }
            }
            return _appIcon;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyWindowChrome();
        }

        /// <summary>Rounded corners + dark frame hints, both optional on older builds.</summary>
        protected void ApplyWindowChrome()
        {
            try
            {
                int round = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(this.Handle, 33, ref round, 4);
            }
            catch { }

            try
            {
                int dark = (Ui.Theme == UiTheme.Light) ? 0 : 1;
                DwmSetWindowAttribute(this.Handle, 20, ref dark, 4);
            }
            catch { }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_GETMINMAXINFO)
            {
                try
                {
                    Rectangle wa = Screen.FromHandle(this.Handle).WorkingArea;
                    MINMAXINFO mmi = (MINMAXINFO)Marshal.PtrToStructure(m.LParam, typeof(MINMAXINFO));
                    mmi.ptMaxPosition.X = 0;
                    mmi.ptMaxPosition.Y = 0;
                    mmi.ptMaxSize.X = wa.Width;
                    mmi.ptMaxSize.Y = wa.Height;
                    Size mn = this.MinimumSize;
                    if (mn.Width > 0) mmi.ptMinTrackSize.X = mn.Width;
                    if (mn.Height > 0) mmi.ptMinTrackSize.Y = mn.Height;
                    Size mx = this.MaximumSize;
                    if (mx.Width > 0) mmi.ptMaxTrackSize.X = mx.Width;
                    if (mx.Height > 0) mmi.ptMaxTrackSize.Y = mx.Height;
                    Marshal.StructureToPtr(mmi, m.LParam, false);
                    return;
                }
                catch { }
            }

            if (m.Msg == WM_NCHITTEST)
            {
                Point p = this.PointToClient(PointFromLParam(m.LParam));
                int edge = Edge;

                if (AllowResize && this.WindowState == FormWindowState.Normal)
                {
                    bool l = p.X < edge;
                    bool r = p.X >= this.ClientSize.Width - edge;
                    bool t = p.Y < edge;
                    bool b = p.Y >= this.ClientSize.Height - edge;
                    int ht = 0;
                    if (t && l) ht = 13;        // HTTOPLEFT
                    else if (t && r) ht = 14;   // HTTOPRIGHT
                    else if (b && l) ht = 16;   // HTBOTTOMLEFT
                    else if (b && r) ht = 17;   // HTBOTTOMRIGHT
                    else if (l) ht = 10;        // HTLEFT
                    else if (r) ht = 11;        // HTRIGHT
                    else if (t) ht = 12;        // HTTOP
                    else if (b) ht = 15;        // HTBOTTOM
                    if (ht != 0)
                    {
                        m.Result = (IntPtr)ht;
                        return;
                    }
                }

                // The caption band behaves like a native title bar: dragging
                // moves the window, double-click maximises, right-click shows
                // the system menu. Child buttons keep their own hit testing.
                if (p.Y >= 0 && p.Y < CapH)
                {
                    m.Result = (IntPtr)2; // HTCAPTION
                    return;
                }
            }

            base.WndProc(ref m);
        }

        private static Point PointFromLParam(IntPtr l)
        {
            long v = l.ToInt64();
            int x = (int)(short)(v & 0xFFFF);
            int y = (int)(short)((v >> 16) & 0xFFFF);
            return new Point(x, y);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;

            using (SolidBrush b = new SolidBrush(Ui.Bg))
            {
                g.FillRectangle(b, this.ClientRectangle);
            }
            DrawWindowBorder(g);
        }

        protected void DrawWindowBorder(Graphics g)
        {
            Rectangle r = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            if (r.Width < 4 || r.Height < 4) return;

            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Ui.Round(r, Ui.Z(9)))
            {
                using (Pen pen = new Pen(Ui.Line, 1F))
                {
                    g.DrawPath(pen, p);
                }
            }
            g.SmoothingMode = old;
        }

        /// <summary>Repaints the caption band after a theme change.</summary>
        protected void RefreshChrome()
        {
            this.BackColor = Ui.Bg;
            this.ForeColor = Ui.Fg;
            ApplyWindowChrome();
            this.Invalidate(true);
        }
    }
}
