using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WifiList
{
    internal sealed class MainForm : ChromeForm
    {
        private const int PadSide = 16;

        private WifiSnapshot _snap;
        private System.Windows.Forms.Timer _lateTimer;
        private int _lateTries;
        private readonly HashSet<string> _revealed = new HashSet<string>(StringComparer.Ordinal);
        private string _sub = "";
        private bool _allShown;
        private bool _busy;

        private GlyphButton _btnRefresh;
        private GlyphButton _btnTheme;
        private GlyphButton _btnLang;
        private GlyphButton _btnAbout;
        private GlyphButton _btnMin;
        private GlyphButton _btnClose;

        private Button _btnAdd;
        private Button _btnDelete;
        private Button _btnToggleAll;
        private Button _btnExport;

        private StatsPanel _stats;
        private CurrentPanel _current;
        private Panel _searchHost;
        private TextBox _txtSearch;
        private GlyphButton _btnClear;
        private ChipToggle _chipKey;
        private DataGridView _grid;
        private Label _lblStatus;
        private Label _lblHint;
        private ToolTip _tips;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string appName, string idList);

        public MainForm()
        {
            this.Text = L.T("app.title") + " v" + Ui.Version;
            this.CaptionHeight = 48;
            this.ClientSize = new Size(Ui.Z(980), Ui.Z(700));
            this.MinimumSize = new Size(Ui.Z(860), Ui.Z(600));
            this.StartPosition = FormStartPosition.CenterScreen;

            // Submenus build their own drop-downs; without a global renderer
            // they would fall back to the light system colours.
            ToolStripManager.Renderer = Ui.MenuRenderer();

            BuildUi();

            this.Load += delegate { Reload(); };
            this.Resize += delegate { LayoutChildren(); };
        }

        // ------------------------------------------------------------------
        // construction
        // ------------------------------------------------------------------

        private void BuildUi()
        {
            _tips = new ToolTip();
            _tips.AutoPopDelay = 9000;

            _btnRefresh = new GlyphButton(GlyphKind.Refresh);
            _btnRefresh.Click += delegate { Reload(); };
            this.Controls.Add(_btnRefresh);

            _btnTheme = new GlyphButton(GlyphKind.Sun);
            _btnTheme.Click += delegate { ToggleTheme(); };
            this.Controls.Add(_btnTheme);

            _btnLang = new GlyphButton(GlyphKind.Globe);
            _btnLang.Click += delegate { ShowLangMenu(); };
            this.Controls.Add(_btnLang);

            _btnAbout = new GlyphButton(GlyphKind.Info);
            _btnAbout.Click += delegate { ShowAbout(); };
            this.Controls.Add(_btnAbout);

            _btnMin = new GlyphButton(GlyphKind.Minimum);
            _btnMin.Click += delegate { this.WindowState = FormWindowState.Minimized; };
            this.Controls.Add(_btnMin);

            _btnClose = new GlyphButton(GlyphKind.Close);
            _btnClose.DangerHover = true;
            _btnClose.Click += delegate { this.Close(); };
            this.Controls.Add(_btnClose);

            _stats = new StatsPanel();
            this.Controls.Add(_stats);

            _current = new CurrentPanel();
            this.Controls.Add(_current);

            _searchHost = new Panel();
            _searchHost.BackColor = Ui.Bg;
            _searchHost.Paint += PaintSearchHost;
            this.Controls.Add(_searchHost);

            _txtSearch = new TextBox();
            _txtSearch.BorderStyle = BorderStyle.None;
            _txtSearch.Font = Ui.FSub;
            _txtSearch.TextChanged += delegate
            {
                _btnClear.Visible = _txtSearch.TextLength > 0;
                RebuildGrid();
            };
            _searchHost.Controls.Add(_txtSearch);

            // small "x" inside the search field that clears the query
            _btnClear = new GlyphButton(GlyphKind.Close);
            _btnClear.BackColor = Ui.Panel;
            _btnClear.Visible = false;
            _btnClear.TabStop = false;
            _btnClear.Click += delegate
            {
                _txtSearch.Text = "";
                _txtSearch.Focus();
            };
            _searchHost.Controls.Add(_btnClear);

            _chipKey = new ChipToggle();
            _chipKey.Text = L.T("chip.onlyKey");
            _chipKey.Toggled += delegate { RebuildGrid(); };
            this.Controls.Add(_chipKey);

            _btnAdd = Ui.MakeButton(L.T("btn.add"), false);
            _btnAdd.Click += OnAdd;
            this.Controls.Add(_btnAdd);

            _btnDelete = Ui.MakeButton(L.T("btn.delete"), false);
            _btnDelete.Click += OnDelete;
            this.Controls.Add(_btnDelete);

            _btnToggleAll = Ui.MakeButton(_allShown ? L.T("btn.hideAll") : L.T("btn.showAll"), false);
            _btnToggleAll.Click += delegate { SetAllRevealed(!_allShown); };
            this.Controls.Add(_btnToggleAll);

            _btnExport = Ui.MakeButton(L.T("btn.export"), true);
            _btnExport.Click += OnExport;
            this.Controls.Add(_btnExport);

            _grid = new DataGridView();
            ConfigureGrid();
            this.Controls.Add(_grid);

            _lblStatus = new Label();
            _lblStatus.Font = Ui.FSub;
            _lblStatus.AutoEllipsis = true;
            _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            _lblStatus.Text = "";
            this.Controls.Add(_lblStatus);

            _lblHint = new Label();
            _lblHint.Font = Ui.FSub;
            _lblHint.AutoEllipsis = true;
            _lblHint.TextAlign = ContentAlignment.MiddleRight;
            _lblHint.Text = "";
            this.Controls.Add(_lblHint);

            ApplyTheme();
            LayoutChildren();
        }

        private void ConfigureGrid()
        {
            _grid.BorderStyle = BorderStyle.None;
            _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            _grid.EnableHeadersVisualStyles = false;
            _grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            _grid.ColumnHeadersDefaultCellStyle.Font = Ui.FCardLabel;
            _grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            _grid.ColumnHeadersHeight = Ui.Z(34);
            _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            _grid.RowHeadersVisible = false;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.AllowUserToResizeColumns = false;
            _grid.AllowUserToOrderColumns = false;
            _grid.ReadOnly = true;
            _grid.MultiSelect = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.RowTemplate.Height = Ui.Z(34);
            _grid.ScrollBars = ScrollBars.Vertical;
            _grid.DefaultCellStyle.Font = Ui.FGrid;
            _grid.DefaultCellStyle.Padding = new Padding(Ui.Z(8), 0, Ui.Z(8), 0);
            _grid.CellDoubleClick += OnCellDoubleClick;
            _grid.CellMouseDown += OnCellMouseDown;
            _grid.HandleCreated += delegate { ApplyScrollTheme(); };

            DataGridViewTextBoxColumn c1 = new DataGridViewTextBoxColumn();
            c1.HeaderText = L.T("grid.name");
            c1.FillWeight = 39F;
            c1.SortMode = DataGridViewColumnSortMode.NotSortable;
            _grid.Columns.Add(c1);

            DataGridViewTextBoxColumn c2 = new DataGridViewTextBoxColumn();
            c2.HeaderText = L.T("grid.signal");
            c2.FillWeight = 10F;
            c2.SortMode = DataGridViewColumnSortMode.NotSortable;
            _grid.Columns.Add(c2);

            DataGridViewTextBoxColumn c3 = new DataGridViewTextBoxColumn();
            c3.HeaderText = L.T("grid.security");
            c3.FillWeight = 20F;
            c3.SortMode = DataGridViewColumnSortMode.NotSortable;
            _grid.Columns.Add(c3);

            DataGridViewTextBoxColumn c4 = new DataGridViewTextBoxColumn();
            c4.HeaderText = L.T("grid.mode");
            c4.FillWeight = 13F;
            c4.SortMode = DataGridViewColumnSortMode.NotSortable;
            _grid.Columns.Add(c4);

            DataGridViewTextBoxColumn c5 = new DataGridViewTextBoxColumn();
            c5.HeaderText = L.T("grid.key");
            c5.FillWeight = 18F;
            c5.SortMode = DataGridViewColumnSortMode.NotSortable;
            _grid.Columns.Add(c5);
        }

        private void PaintSearchHost(object sender, PaintEventArgs e)
        {
            Ui.FillRound(e.Graphics,
                new Rectangle(0, 0, _searchHost.Width - 1, _searchHost.Height - 1),
                Ui.Z(8), Ui.Panel, Ui.Line);
        }

        private void LayoutChildren()
        {
            if (_grid == null) return;

            // Everything below is expressed in device pixels: the design
            // constants are 96 dpi values run through Ui.Z().
            int pad = Ui.Z(PadSide);
            int x = pad;
            int w = this.ClientSize.Width - pad * 2;
            if (w < Ui.Z(240)) return;

            int capH = CapH;

            // caption buttons
            int btnW = Ui.Z(30);
            int btnH = Ui.Z(26);
            int by = (capH - btnH) / 2;
            int bx = x + w;
            _btnClose.SetBounds(bx - btnW, by, btnW, btnH); bx -= btnW + Ui.Z(2);
            _btnMin.SetBounds(bx - btnW, by, btnW, btnH); bx -= btnW + Ui.Z(2);
            _btnTheme.SetBounds(bx - btnW, by, btnW, btnH); bx -= btnW + Ui.Z(2);
            _btnAbout.SetBounds(bx - btnW, by, btnW, btnH); bx -= btnW + Ui.Z(2);
            _btnLang.SetBounds(bx - btnW, by, btnW, btnH); bx -= btnW + Ui.Z(2);
            _btnRefresh.SetBounds(bx - btnW, by, btnW, btnH);

            int barH = Ui.Z(30);

            int y = capH + Ui.Z(12);
            _stats.SetBounds(x, y, w, Ui.Z(84));
            y += Ui.Z(84) + Ui.Z(10);
            _current.SetBounds(x, y, w, Ui.Z(98));
            y += Ui.Z(98) + Ui.Z(12);

            int searchW = Ui.Z(250);
            _searchHost.SetBounds(x, y, searchW, barH);

            int clearS = Ui.Z(19);
            int clearX = searchW - Ui.Z(7) - clearS;
            _btnClear.SetBounds(clearX, (barH - clearS) / 2, clearS, clearS);

            int textRight = clearX - Ui.Z(2);
            _txtSearch.SetBounds(Ui.Z(11), Math.Max(0, (barH - _txtSearch.PreferredHeight) / 2),
                Math.Max(Ui.Z(40), textRight - Ui.Z(11)), _txtSearch.PreferredHeight);

            // button widths follow the caption, because "Delete" and "ลบ" and
            // "删除" are far from the same length
            int chipW = Math.Max(Ui.Z(108), Ui.TextW(_chipKey.Text, Ui.FSub) + Ui.Z(46));
            _chipKey.SetBounds(x + searchW + Ui.Z(10), y, chipW, barH);

            int rx = x + w;
            int bw = Math.Max(Ui.Z(126), Ui.TextW(L.T("btn.export"), Ui.FBtnBold) + Ui.Z(28));
            _btnExport.SetBounds(rx - bw, y, bw, barH); rx -= bw + Ui.Z(8);
            bw = Math.Max(Ui.Z(104), Math.Max(Ui.TextW(L.T("btn.showAll"), Ui.FBtn),
                Ui.TextW(L.T("btn.hideAll"), Ui.FBtn)) + Ui.Z(28));
            _btnToggleAll.SetBounds(rx - bw, y, bw, barH); rx -= bw + Ui.Z(8);
            bw = Math.Max(Ui.Z(62), Ui.TextW(L.T("btn.delete"), Ui.FBtn) + Ui.Z(28));
            _btnDelete.SetBounds(rx - bw, y, bw, barH); rx -= bw + Ui.Z(8);
            bw = Math.Max(Ui.Z(92), Ui.TextW(L.T("btn.add"), Ui.FBtn) + Ui.Z(28));
            _btnAdd.SetBounds(rx - bw, y, bw, barH);

            y += barH + Ui.Z(10);

            int lineH = _lblStatus.Font.Height + Ui.Z(4);
            int statusY = this.ClientSize.Height - Ui.Z(8) - lineH;
            int gridH = statusY - Ui.Z(8) - y;
            if (gridH < Ui.Z(80)) gridH = Ui.Z(80);
            _grid.SetBounds(x, y, w, gridH);

            int hintW = Math.Max(Ui.Z(140), Ui.TextW(L.T("hint.bar"), Ui.FSub) + Ui.Z(6));
            hintW = Math.Min(hintW, Math.Max(Ui.Z(140), w / 2));
            _lblStatus.SetBounds(x, statusY, Math.Max(Ui.Z(120), w - hintW - Ui.Z(12)), lineH);
            _lblHint.SetBounds(x + w - hintW, statusY, hintW, lineH);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;

            int pad = Ui.Z(PadSide);
            int capH = CapH;
            int logo = Ui.Z(30);
            Ui.DrawLogo(g, new Rectangle(pad, (capH - logo) / 2, logo, logo));

            int textX = pad + Ui.Z(40);
            int textW = Math.Max(Ui.Z(120), this.ClientSize.Width - textX - Ui.Z(260));

            Ui.Text(g, L.T("app.title"), Ui.FTitle, Ui.Fg,
                new Rectangle(textX, Ui.Z(5), textW, Ui.FTitle.Height + Ui.Z(2)), Ui.LeftTop);

            Ui.Text(g, _sub, Ui.FMono, Ui.Mute,
                new Rectangle(textX + Ui.Z(1), Ui.Z(5) + Ui.FTitle.Height + Ui.Z(1), textW,
                    Ui.FMono.Height + Ui.Z(2)), Ui.LeftTop);

            using (Pen pen = new Pen(Ui.Line, 1F))
            {
                g.DrawLine(pen, pad, capH - 1, this.ClientSize.Width - pad, capH - 1);
            }
        }

        // ------------------------------------------------------------------
        // theme
        // ------------------------------------------------------------------

        private void ToggleTheme()
        {
            Ui.SetTheme(Ui.Theme == UiTheme.Dark ? UiTheme.Light : UiTheme.Dark);
            Ui.SaveTheme();

            ApplyTheme();
            ApplyWindowChrome();
            if (_snap != null) RebuildGrid();
        }

        private void ApplyTheme()
        {
            this.BackColor = Ui.Bg;
            this.ForeColor = Ui.Fg;

            bool dark = Ui.Theme == UiTheme.Dark;
            _btnTheme.Kind = dark ? GlyphKind.Sun : GlyphKind.Moon;
            _btnTheme.Invalidate();
            _tips.SetToolTip(_btnTheme, dark ? L.T("tip.themeLight") : L.T("tip.themeDark"));
            _tips.SetToolTip(_btnMin, L.T("tip.min"));
            _tips.SetToolTip(_btnClose, L.T("tip.close"));
            _tips.SetToolTip(_btnRefresh, L.T("tip.refresh"));
            _tips.SetToolTip(_btnAdd, L.T("tip.add"));
            _tips.SetToolTip(_btnDelete, L.T("tip.delete"));
            _tips.SetToolTip(_btnLang, L.T("tip.lang"));
            _tips.SetToolTip(_btnAbout, L.T("tip.about"));

            _searchHost.BackColor = Ui.Bg;
            _searchHost.Invalidate();
            _txtSearch.BackColor = Ui.Panel;
            _txtSearch.ForeColor = Ui.Fg;
            _btnClear.BackColor = Ui.Panel;
            _btnClear.Invalidate();
            _tips.SetToolTip(_btnClear, L.T("tip.clear"));

            _chipKey.BackColor = Ui.Bg;
            _chipKey.Invalidate();

            Ui.StyleButton(_btnAdd, false);
            Ui.StyleButton(_btnToggleAll, false);
            Ui.StyleButton(_btnExport, true);
            Ui.StyleButton(_btnDelete, false);
            _btnDelete.ForeColor = Ui.Danger;
            _btnDelete.FlatAppearance.MouseOverBackColor = Ui.Mix(Ui.Danger, Ui.Panel, 0.84);
            _btnDelete.FlatAppearance.MouseDownBackColor = Ui.Mix(Ui.Danger, Ui.Panel, 0.72);

            _stats.ApplyTheme();
            _current.ApplyTheme();

            StyleGrid();
            ApplyScrollTheme();

            _lblStatus.BackColor = Ui.Bg;
            _lblStatus.ForeColor = Ui.Mute;
            _lblHint.BackColor = Ui.Bg;
            _lblHint.ForeColor = Ui.Mute;

            this.Invalidate(true);
        }

        private void StyleGrid()
        {
            _grid.BackgroundColor = Ui.Panel;
            _grid.GridColor = Ui.Line;
            _grid.ColumnHeadersDefaultCellStyle.BackColor = Ui.PanelHi;
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = Ui.Dim;
            _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Ui.PanelHi;

            _grid.DefaultCellStyle.BackColor = Ui.Panel;
            _grid.DefaultCellStyle.ForeColor = Ui.Fg;
            _grid.DefaultCellStyle.SelectionBackColor = Ui.SelBg;
            _grid.DefaultCellStyle.SelectionForeColor = Ui.Fg;

            _grid.AlternatingRowsDefaultCellStyle.BackColor = Ui.RowAlt;
            _grid.AlternatingRowsDefaultCellStyle.ForeColor = Ui.Fg;
            _grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = Ui.SelBg;
            _grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = Ui.Fg;
        }

        /// <summary>Native grid scrollbars ignore WinForms colours; ask uxtheme for the dark variant.</summary>
        private void ApplyScrollTheme()
        {
            if (_grid == null) return;
            bool dark = Ui.Theme == UiTheme.Dark;
            for (int i = 0; i < _grid.Controls.Count; i++)
            {
                try
                {
                    SetWindowTheme(_grid.Controls[i].Handle, dark ? "DarkMode_Explorer" : null, null);
                }
                catch { }
            }
        }

        // ------------------------------------------------------------------
        // data
        // ------------------------------------------------------------------

        private void Reload()
        {
            if (_busy) return;
            _busy = true;
            _lblStatus.Text = L.T("status.loading");
            _lblHint.Text = "";
            this.Cursor = Cursors.WaitCursor;
            Application.DoEvents();

            WifiSnapshot snap = null;
            string error = null;
            try
            {
                snap = WifiCollector.Load();
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            this.Cursor = Cursors.Default;

            if (snap == null)
            {
                snap = new WifiSnapshot();
                snap.Error = error == null ? L.T("status.readFail") : error;
            }

            _snap = snap;
            _revealed.Clear();
            _allShown = false;
            _btnToggleAll.Text = L.T("btn.showAll");

            _stats.Bind(_snap);
            _current.Bind(_snap);

            UpdateSub();
            this.Invalidate();

            RebuildGrid();

            if (_snap.Error != null && _snap.Error.Length > 0)
            {
                _lblStatus.Text = _snap.Error;
            }

            _busy = false;

            // If the adapter had no address yet, look again shortly: this is
            // what recovers the IP/gateway/DNS block after a network switch
            // without the user having to restart the app.
            EnsureLateRefresh();
        }

        private List<WifiEntry> Filtered()
        {
            List<WifiEntry> list = new List<WifiEntry>();
            if (_snap == null) return list;

            string q = _txtSearch.Text.Trim();
            bool onlyKey = _chipKey.On;

            for (int i = 0; i < _snap.Nets.Count; i++)
            {
                WifiEntry n = _snap.Nets[i];
                if (onlyKey && !n.HasKey) continue;
                if (q.Length > 0 && n.Ssid.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) < 0) continue;
                list.Add(n);
            }
            return list;
        }

        private void RebuildGrid()
        {
            if (_grid == null || _snap == null) return;

            List<WifiEntry> list = Filtered();
            string keep = null;
            if (_grid.CurrentRow != null)
            {
                WifiEntry cur = _grid.CurrentRow.Tag as WifiEntry;
                if (cur != null) keep = cur.Ssid;
            }

            _grid.SuspendLayout();
            _grid.Rows.Clear();
            int restoreRow = -1;
            for (int i = 0; i < list.Count; i++)
            {
                WifiEntry n = list[i];

                string name = (n.Current ? "\u2605  " : "") + n.Ssid;
                string sig = n.HasSignal ? n.Signal.ToString(CultureInfo.InvariantCulture) + "%" : "\u2014";
                string sec = n.SecurityText();
                string mode = n.ModeLabel();
                string keyText;
                if (!n.HasKey)
                {
                    keyText = L.T("cell.noKey");
                }
                else if (_revealed.Contains(n.Ssid))
                {
                    keyText = n.Key.Length > 0 ? n.Key : L.T("cell.notSaved");
                }
                else
                {
                    keyText = "\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022";
                }

                int idx = _grid.Rows.Add(name, sig, sec, mode, keyText);
                DataGridViewRow row = _grid.Rows[idx];
                row.Tag = n;
                row.Cells[2].Style.ForeColor = Ui.TierColor(n.Tier());
                row.Cells[1].Style.ForeColor = n.HasSignal && n.Signal >= 40 ? Ui.Fg : Ui.Mute;
                if (n.Current)
                {
                    row.Cells[0].Style.ForeColor = Ui.Accent;
                }
                if (!n.HasKey)
                {
                    row.Cells[4].Style.ForeColor = Ui.Mute;
                }
                else if (_revealed.Contains(n.Ssid))
                {
                    row.Cells[4].Style.ForeColor = Ui.Ok;
                    row.Cells[4].Style.Font = Ui.FVal;
                }

                if (keep != null && string.Equals(keep, n.Ssid, StringComparison.Ordinal))
                {
                    restoreRow = idx;
                }
            }
            _grid.ResumeLayout();

            if (restoreRow >= 0 && restoreRow < _grid.Rows.Count)
            {
                try { _grid.CurrentCell = _grid.Rows[restoreRow].Cells[0]; }
                catch { }
            }

            _lblStatus.Text = _snap.Nets.Count == 0
                ? L.T("status.empty")
                : L.F("status.count", _snap.Nets.Count, list.Count);
            _lblHint.Text = L.T("hint.bar");

            ApplyScrollTheme();
        }

        private WifiEntry Selected()
        {
            if (_grid == null || _grid.CurrentRow == null) return null;
            return _grid.CurrentRow.Tag as WifiEntry;
        }

        private void SetAllRevealed(bool on)
        {
            if (_snap == null) return;
            _allShown = on;
            _btnToggleAll.Text = on ? L.T("btn.hideAll") : L.T("btn.showAll");
            if (on)
            {
                for (int i = 0; i < _snap.Nets.Count; i++)
                {
                    if (_snap.Nets[i].HasKey && _snap.Nets[i].Key.Length > 0)
                    {
                        _revealed.Add(_snap.Nets[i].Ssid);
                    }
                }
            }
            else
            {
                _revealed.Clear();
            }
            RebuildGrid();
        }

        // ------------------------------------------------------------------
        // grid interaction
        // ------------------------------------------------------------------

        private void OnCellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _grid.Rows.Count) return;
            DataGridViewRow row = _grid.Rows[e.RowIndex];
            WifiEntry n = row.Tag as WifiEntry;
            if (n == null || !n.HasKey) return;

            if (_revealed.Contains(n.Ssid)) _revealed.Remove(n.Ssid);
            else _revealed.Add(n.Ssid);

            RebuildGrid();
            if (e.RowIndex < _grid.Rows.Count)
            {
                _grid.Rows[e.RowIndex].Selected = true;
            }
        }

        private void OnCellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            if (e.RowIndex < 0 || e.RowIndex >= _grid.Rows.Count) return;

            DataGridViewRow row = _grid.Rows[e.RowIndex];
            WifiEntry n = row.Tag as WifiEntry;
            if (n == null) return;

            row.Selected = true;

            bool isCurrent = n.Current && _snap != null && _snap.Current.Connected;

            ContextMenuStrip menu = new ContextMenuStrip();
            Ui.StyleMenu(menu);

            // ---- 1. connection: only one direction can apply, so one row ----
            if (isCurrent)
            {
                ToolStripMenuItem miOff = Ui.MenuItem(L.T("menu.disconnect"));
                miOff.Click += delegate { DisconnectNow(); };
                menu.Items.Add(miOff);
            }
            else
            {
                ToolStripMenuItem miOn = Ui.MenuItem(L.T("menu.connect"));
                miOn.Click += delegate { ConnectEntry(n); };
                menu.Items.Add(miOn);
            }

            menu.Items.Add(Ui.MenuSeparator());

            // ---- 2. clipboard ----
            ToolStripMenuItem miCopy = Ui.MenuItem(L.T("menu.copy"));
            ToolStripMenuItem miName = Ui.MenuItem(L.T("menu.copyName"));
            miName.Click += delegate { CopyText(n.Ssid); };
            miCopy.DropDownItems.Add(miName);

            ToolStripMenuItem miKey = Ui.MenuItem(L.T("menu.copyKey"));
            miKey.Enabled = n.HasKey && n.Key.Length > 0;
            miKey.Click += delegate { CopyText(n.Key); };
            miCopy.DropDownItems.Add(miKey);
            menu.Items.Add(miCopy);

            // ---- 3. reveal ----
            if (n.HasKey)
            {
                bool shown = _revealed.Contains(n.Ssid);
                ToolStripMenuItem miToggle = Ui.MenuItem(shown ? L.T("menu.hideKey") : L.T("menu.showKey"));
                miToggle.Click += delegate
                {
                    if (_revealed.Contains(n.Ssid)) _revealed.Remove(n.Ssid);
                    else _revealed.Add(n.Ssid);
                    RebuildGrid();
                };
                menu.Items.Add(miToggle);
            }

            menu.Items.Add(Ui.MenuSeparator());

            // ---- 4. profile maintenance, folded into one submenu ----
            ToolStripMenuItem miCfg = Ui.MenuItem(L.T("menu.config"));
            bool canEdit = CanEditProfile(n);
            ToolStripMenuItem miEdit = Ui.MenuItem(canEdit ? L.T("menu.edit") : L.T("menu.editNo"));
            miEdit.Enabled = canEdit;
            miEdit.Click += delegate { EditEntry(n); };
            miCfg.DropDownItems.Add(miEdit);

            bool isAuto = string.Equals(n.Mode, "auto", StringComparison.OrdinalIgnoreCase);
            ToolStripMenuItem miMode = Ui.MenuItem(isAuto ? L.T("menu.manual") : L.T("menu.auto"));
            miMode.Click += delegate { SetAutoMode(n, !isAuto); };
            miCfg.DropDownItems.Add(miMode);

            miCfg.DropDownItems.Add(Ui.MenuSeparator());

            ToolStripMenuItem miDelete = Ui.MenuItem(L.T("menu.delete"));
            miDelete.ForeColor = Ui.Danger;
            miDelete.Click += delegate { DeleteEntry(n); };
            miCfg.DropDownItems.Add(miDelete);
            menu.Items.Add(miCfg);

            // ---- 5. refresh ----
            ToolStripMenuItem miRefresh = Ui.MenuItem(L.T("menu.refresh"));
            miRefresh.Click += delegate { Reload(); };
            menu.Items.Add(miRefresh);

            menu.Show(_grid, _grid.PointToClient(Cursor.Position));
        }

        /// <summary>
        /// Only PSK style profiles can be rebuilt from this form. Enterprise
        /// (802.1X) and WEP profiles are left to the Windows settings UI.
        /// </summary>
        private static bool CanEditProfile(WifiEntry n)
        {
            if (n == null || n.OneX) return false;
            string a = n.AuthCode;
            return a == "WPA2PSK" || a == "WPA3SAE" || a == "WPAPSK" || a == "OPEN";
        }

        private WifiEntry FindEntry(string ssid)
        {
            if (_snap == null) return null;
            for (int i = 0; i < _snap.Nets.Count; i++)
            {
                if (string.Equals(_snap.Nets[i].Ssid, ssid, StringComparison.Ordinal))
                {
                    return _snap.Nets[i];
                }
            }
            return null;
        }

        /// <summary>Waits for the radio to reach the expected state, pumping the UI meanwhile.</summary>
        private bool WaitState(string ssid, bool wantConnected, int timeoutMs)
        {
            int waited = 0;
            while (waited <= timeoutMs)
            {
                NetworkInfo ci = WifiCollector.GetCurrent();
                if (wantConnected)
                {
                    if (ci.Connected && string.Equals(ci.Ssid, ssid, StringComparison.Ordinal)) return true;
                }
                else if (!ci.Connected || ci.Ssid.Length == 0)
                {
                    // the adapter stays Up after a disconnect, so "no SSID"
                    // is the signal that the radio really let go
                    return true;
                }

                if (waited >= timeoutMs) break;
                System.Threading.Thread.Sleep(400);
                waited += 400;
                _lblStatus.Text = wantConnected
                    ? L.F("status.waitConnect", Q(ssid), waited / 1000)
                    : L.F("status.waitDisconnect", waited / 1000);
                Application.DoEvents();
            }
            return false;
        }

        /// <summary>
        /// Waits until the physical adapter holds an address. Association
        /// completes long before DHCP does, so reading the card too early
        /// showed "-" for IP, gateway, DNS and link speed even though the
        /// network was fine — and it only came back after a restart.
        /// </summary>
        private bool WaitForAddress(int timeoutMs)
        {
            if (WifiCollector.HasAddress()) return true;
            int waited = 0;
            while (waited < timeoutMs)
            {
                _lblStatus.Text = L.T("status.waitAddress");
                Application.DoEvents();
                System.Threading.Thread.Sleep(150);
                waited += 150;
                if (WifiCollector.HasAddress()) return true;
            }
            return false;
        }

        /// <summary>
        /// Starts a short watch for late-arriving connection details. The
        /// adapter can still be coming up a second after the switch (this is
        /// also the window in which a virtual adapter could masquerade as the
        /// connection), so one delayed re-read fixes the card without asking
        /// the user to restart the app.
        /// </summary>
        private void EnsureLateRefresh()
        {
            if (_lateTimer != null && _lateTimer.Enabled) return;   // already watching
            if (_snap == null || !_snap.Current.Connected) return;
            if (_snap.Current.Ip != null && _snap.Current.Ip.Length > 0) return;
            if (_lateTries >= 4) return;

            if (_lateTimer == null)
            {
                _lateTimer = new System.Windows.Forms.Timer();
                _lateTimer.Interval = 1500;
                _lateTimer.Tick += delegate { OnLateRefresh(); };
            }
            _lateTimer.Start();
        }

        private void OnLateRefresh()
        {
            if (this.IsDisposed || _lateTimer == null)
            {
                if (_lateTimer != null) _lateTimer.Stop();
                return;
            }
            _lateTries++;
            bool settled = _snap == null || !_snap.Current.Connected ||
                (_snap.Current.Ip != null && _snap.Current.Ip.Length > 0);
            if (settled || _lateTries >= 4)
            {
                _lateTimer.Stop();
                return;
            }
            if (!_busy) Reload();
        }

        private void ConnectEntry(WifiEntry n)
        {
            if (n == null || _busy) return;

            _busy = true;
            this.Cursor = Cursors.WaitCursor;
            Application.DoEvents();

            string iface = WifiCollector.WirelessInterfaceName();
            NetshResult last = null;
            bool ok = false;

            // Two attempts. The first names the adapter explicitly (netsh fails
            // with "无法连接…" when it has to guess between the physical card and
            // the Wi-Fi Direct adapter); the second drops the old association
            // first, because switching networks often fails while the radio is
            // still tearing down the previous one even though the Windows
            // flyout would have managed it.
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (attempt > 0)
                {
                    _lblStatus.Text = L.T("status.retryDrop");
                    Application.DoEvents();
                    WifiCollector.Disconnect();
                    WaitState("", false, 3000);
                }

                _lblStatus.Text = attempt == 0
                    ? L.F("status.connecting", Q(n.Ssid))
                    : L.F("status.retrying", Q(n.Ssid));
                Application.DoEvents();

                last = attempt == 0
                    ? WifiCollector.Connect(n.Ssid, iface)
                    : WifiCollector.Connect(n.Ssid);

                ok = WaitState(n.Ssid, true, attempt == 0 ? 8000 : 10000);
                if (ok) break;
            }

            this.Cursor = Cursors.Default;
            _busy = false;

            if (ok)
            {
                // Association is not a usable connection: DHCP still has to
                // hand out the address, and until it does the card would show
                // dashes for IP, gateway, DNS and link speed. Wait for the
                // address, then read everything at once.
                WaitForAddress(6000);
                Reload();
                _lblStatus.Text = L.F("status.connected", Q(n.Ssid));
                return;
            }

            Reload();

            string detail = last == null ? "" : FirstLines(last.Text, 5);
            string msg = "";
            if (detail.Length > 0)
            {
                msg += detail + "\r\n\r\n";
            }
            if (last != null && last.Code != 0)
            {
                msg += L.F("dlg.retCode", last.Code) + "\r\n\r\n";
            }
            msg += L.T("dlg.connectReasons") + "\r\n\r\n" + L.T("dlg.connectTail");
            Prompt.Info(this, L.T("dlg.connectFail"), msg);
        }

        private void DisconnectNow()
        {
            if (_busy) return;

            _busy = true;
            this.Cursor = Cursors.WaitCursor;
            _lblStatus.Text = L.T("status.disconnecting");
            Application.DoEvents();

            NetshResult r = WifiCollector.Disconnect();
            bool ok = WaitState("", false, 8000);

            this.Cursor = Cursors.Default;
            _busy = false;
            Reload();

            if (ok)
            {
                _lblStatus.Text = L.T("status.disconnected");
            }
            else
            {
                Prompt.Info(this, L.T("dlg.disconnectFail"),
                    FirstLines(r.Text, 6) + "\r\n\r\n" + L.T("dlg.disconnectTip"));
            }
        }

        private void SetAutoMode(WifiEntry n, bool auto)
        {
            if (n == null || _busy) return;

            _busy = true;
            this.Cursor = Cursors.WaitCursor;
            NetshResult r = WifiCollector.SetConnectionMode(n.Ssid, auto);
            this.Cursor = Cursors.Default;
            _busy = false;

            Reload();

            WifiEntry after = FindEntry(n.Ssid);
            bool ok = after != null &&
                string.Equals(after.Mode, auto ? "auto" : "manual", StringComparison.OrdinalIgnoreCase);

            if (ok)
            {
                _lblStatus.Text = L.F("status.modeSet", Q(n.Ssid), auto ? L.T("m.auto") : L.T("m.manual"));
            }
            else
            {
                Prompt.Info(this, L.T("dlg.modeFail"),
                    FirstLines(r.Text, 6) + "\r\n\r\n" + L.T("dlg.modeFailTip"));
            }
        }

        /// <summary>
        /// Rebuilds an existing profile from the edit form. The old profile is
        /// exported first so a failed save can be rolled back.
        /// </summary>
        private void EditEntry(WifiEntry n)
        {
            if (n == null || _busy) return;

            using (AddWifiForm dlg = new AddWifiForm(n))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                string oldSsid = n.Ssid;
                bool renamed = !string.Equals(dlg.Ssid, oldSsid, StringComparison.Ordinal);

                if (renamed && FindEntry(dlg.Ssid) != null)
                {
                    if (!Prompt.Ask(this, L.T("dlg.dupTitle"),
                        L.F("dlg.dupAsk", Q(dlg.Ssid)), L.T("btn.overwrite"), true))
                    {
                        return;
                    }
                }

                _busy = true;
                this.Cursor = Cursors.WaitCursor;
                string backup = WifiCollector.BackupProfile(oldSsid);

                _lblStatus.Text = L.F("status.saving", Q(dlg.Ssid));
                Application.DoEvents();

                NetshResult r = WifiCollector.AddProfile(dlg.Ssid, dlg.Auth, dlg.Enc,
                    dlg.Key, dlg.HiddenNet, dlg.AutoMode);

                // Some builds refuse to overwrite an existing profile, in which
                // case the old one is removed and the new one written again.
                if (!r.Ok && !renamed)
                {
                    WifiCollector.DeleteProfile(oldSsid);
                    r = WifiCollector.AddProfile(dlg.Ssid, dlg.Auth, dlg.Enc,
                        dlg.Key, dlg.HiddenNet, dlg.AutoMode);
                }

                bool saved = false;
                if (r.Ok)
                {
                    WifiSnapshot check = WifiCollector.Load();
                    if (check != null)
                    {
                        for (int i = 0; i < check.Nets.Count; i++)
                        {
                            if (string.Equals(check.Nets[i].Ssid, dlg.Ssid, StringComparison.Ordinal))
                            {
                                saved = string.Equals(check.Nets[i].AuthCode,
                                    dlg.Auth.ToUpperInvariant(), StringComparison.Ordinal);
                                break;
                            }
                        }
                    }
                }

                NetshResult delOld = null;
                if (saved && renamed)
                {
                    delOld = WifiCollector.DeleteProfile(oldSsid);
                }

                if (saved && dlg.ConnectNow)
                {
                    WifiCollector.Connect(dlg.Ssid, WifiCollector.WirelessInterfaceName());
                    WaitState(dlg.Ssid, true, 12000);
                }

                this.Cursor = Cursors.Default;
                _busy = false;
                Reload();

                if (!saved)
                {
                    string extra = "";
                    if (backup.Length > 0)
                    {
                        NetshResult rb = WifiCollector.RestoreProfile(backup);
                        extra = rb.Ok
                            ? "\r\n\r\n" + L.T("dlg.rolledBack")
                            : "\r\n\r\n" + L.T("dlg.rollbackFail");
                    }

                    Prompt.Info(this, L.T("dlg.saveFail"),
                        FirstLines(r.Text, 8) + extra + "\r\n\r\n" + L.T("dlg.saveReasons"));
                }
                else
                {
                    _lblStatus.Text = renamed
                        ? L.F("status.renamed", Q(dlg.Ssid))
                        : L.F("status.saved", Q(dlg.Ssid));
                    if (n.Current && !dlg.ConnectNow)
                    {
                        _lblStatus.Text += L.T("status.savedLive");
                    }
                }

                // The old profile is normally gone by now. If it is not, say so
                // instead of leaving the user with two look-alike rows and no
                // explanation.
                if (delOld != null && !delOld.Ok)
                {
                    Prompt.Info(this, L.T("dlg.keepOldTitle"),
                        L.F("dlg.keepOld", Q(oldSsid), Q(dlg.Ssid)) +
                        "\r\n\r\n" + FirstLines(delOld.Text, 6));
                }

                WifiCollector.DropBackup(backup);
            }
        }

        private void CopyText(string text)
        {
            if (text == null || text.Length == 0) return;
            try
            {
                Clipboard.SetText(text);
                _lblStatus.Text = L.T("status.copied");
            }
            catch
            {
                _lblStatus.Text = L.T("status.copyFail");
            }
        }

        // ------------------------------------------------------------------
        // add / delete
        // ------------------------------------------------------------------

        private void OnAdd(object sender, EventArgs e)
        {
            using (AddWifiForm dlg = new AddWifiForm())
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                this.Cursor = Cursors.WaitCursor;
                NetshResult r = WifiCollector.AddProfile(dlg.Ssid, dlg.Auth, dlg.Enc, dlg.Key, dlg.HiddenNet, dlg.AutoMode);
                if (r.Ok && dlg.ConnectNow)
                {
                    WifiCollector.Connect(dlg.Ssid, WifiCollector.WirelessInterfaceName());
                    WaitState(dlg.Ssid, true, 10000);
                }
                this.Cursor = Cursors.Default;

                Reload();

                bool present = false;
                if (_snap != null)
                {
                    for (int i = 0; i < _snap.Nets.Count; i++)
                    {
                        if (string.Equals(_snap.Nets[i].Ssid, dlg.Ssid, StringComparison.Ordinal))
                        {
                            present = true;
                            break;
                        }
                    }
                }

                if (present)
                {
                    _lblStatus.Text = L.F("status.added", Q(dlg.Ssid));
                    if (dlg.ConnectNow)
                    {
                        bool on = _snap != null && _snap.Current.Connected &&
                            string.Equals(_snap.Current.Ssid, dlg.Ssid, StringComparison.Ordinal);
                        _lblStatus.Text += on ? L.T("status.addedOk") : L.T("status.addedNo");
                    }
                }
                else
                {
                    Prompt.Info(this, L.T("dlg.addFail"),
                        FirstLines(r.Text, 8) + "\r\n\r\n" + L.T("dlg.addReasons"));
                }
            }
        }

        private void OnDelete(object sender, EventArgs e)
        {
            WifiEntry n = Selected();
            if (n == null)
            {
                Prompt.Info(this, L.T("dlg.deleteTitle"), L.T("dlg.deletePick"));
                return;
            }
            DeleteEntry(n);
        }

        private void DeleteEntry(WifiEntry n)
        {
            if (n == null || _busy) return;

            string msg = L.F("dlg.deleteAsk", Q(n.Ssid));
            if (n.Current)
            {
                msg += L.T("dlg.deleteAskLive");
            }

            if (!Prompt.Ask(this, L.T("dlg.deleteTitle"), msg, L.T("btn.delete"), true)) return;

            this.Cursor = Cursors.WaitCursor;
            NetshResult r = WifiCollector.DeleteProfile(n.Ssid);
            this.Cursor = Cursors.Default;

            Reload();

            bool still = false;
            if (_snap != null)
            {
                for (int i = 0; i < _snap.Nets.Count; i++)
                {
                    if (string.Equals(_snap.Nets[i].Ssid, n.Ssid, StringComparison.Ordinal))
                    {
                        still = true;
                        break;
                    }
                }
            }

            if (still)
            {
                Prompt.Info(this, L.T("dlg.deleteFail"),
                    FirstLines(r.Text, 8) + "\r\n\r\n" + L.T("dlg.deleteFailTip"));
            }
            else
            {
                _lblStatus.Text = L.F("status.deleted", Q(n.Ssid));
            }
        }

        private static string FirstLines(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return L.T("netsh.noDetail");
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            List<string> keep = new List<string>();
            for (int i = 0; i < lines.Length; i++)
            {
                string s = lines[i].Trim();
                if (s.Length == 0) continue;
                keep.Add(s);
                if (keep.Count >= max) break;
            }
            return keep.Count == 0 ? L.T("netsh.noDetail") : string.Join("\r\n", keep.ToArray());
        }

        private void OnExport(object sender, EventArgs e)
        {
            if (_snap == null || _snap.Nets.Count == 0) return;
            try
            {
                string path = HtmlExporter.DefaultPath();
                HtmlExporter.Export(path, _snap);
                _lblStatus.Text = L.F("status.exported", path);
                HtmlExporter.OpenWithShell(path);
            }
            catch (Exception ex)
            {
                Prompt.Info(this, L.T("dlg.exportFail"), ex.Message);
            }
        }

        // ------------------------------------------------------------------
        // misc
        // ------------------------------------------------------------------

        // ------------------------------------------------------------------
        // language / about
        // ------------------------------------------------------------------

        /// <summary>Quotes a name the way the active language does.</summary>
        private static string Q(string s)
        {
            if (s == null) s = "";
            if (L.Current == UiLang.Zh) return "\u300C" + s + "\u300D";
            return "\u201C" + s + "\u201D";
        }

        private void UpdateSub()
        {
            if (_snap == null) return;
            _sub = L.F("app.sub", Ui.Version, Environment.MachineName, _snap.Nets.Count,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        }

        private void ShowLangMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            Ui.StyleMenu(menu);

            UiLang[] all = new UiLang[] { UiLang.Zh, UiLang.En, UiLang.Th };
            for (int i = 0; i < all.Length; i++)
            {
                UiLang lang = all[i];
                string label = (L.Current == lang ? "\u2713  " : "\u2003  ") + L.NativeName(lang);
                ToolStripMenuItem mi = Ui.MenuItem(label);
                mi.Click += delegate { SwitchLanguage(lang); };
                menu.Items.Add(mi);
            }
            menu.Show(_btnLang, new Point(0, _btnLang.Height + Ui.Z(2)));
        }

        private void SwitchLanguage(UiLang lang)
        {
            if (L.Current == lang) return;
            L.Current = lang;
            L.Save();
            Ui.LoadFonts();
            RebuildUi();
        }

        /// <summary>
        /// Recreates every child control after a language change. Captions and
        /// fonts are bound when a control is built, so the cheapest correct
        /// refresh is to build the window again and re-bind the same snapshot.
        /// </summary>
        private void RebuildUi()
        {
            string query = _txtSearch == null ? "" : _txtSearch.Text;

            while (this.Controls.Count > 0)
            {
                Control c = this.Controls[0];
                this.Controls.RemoveAt(0);
                try { c.Dispose(); } catch { }
            }
            if (_tips != null)
            {
                try { _tips.Dispose(); } catch { }
                _tips = null;
            }

            this.Text = L.T("app.title") + " v" + Ui.Version;
            BuildUi();

            _txtSearch.Text = query;
            if (_snap != null)
            {
                _stats.Bind(_snap);
                _current.Bind(_snap);
                UpdateSub();
                RebuildGrid();
            }
            this.Invalidate(true);
        }

        private void ShowAbout()
        {
            using (AboutForm f = new AboutForm(_snap))
            {
                f.ShowDialog(this);
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape && _txtSearch.Focused && _txtSearch.TextLength > 0)
            {
                _txtSearch.Text = "";
                return true;
            }
            if (keyData == Keys.F5)
            {
                Reload();
                return true;
            }
            if (keyData == Keys.Delete && _grid.Focused)
            {
                WifiEntry n = Selected();
                if (n != null)
                {
                    DeleteEntry(n);
                    return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try
            {
                SendMessage(_txtSearch.Handle, 0x1501, new IntPtr(1), L.T("ph.search"));
            }
            catch { }
        }
    }
}
