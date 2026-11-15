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
        private readonly HashSet<string> _revealed = new HashSet<string>(StringComparer.Ordinal);
        private string _sub = "";
        private bool _allShown;
        private bool _busy;

        private GlyphButton _btnRefresh;
        private GlyphButton _btnTheme;
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
            this.Text = "WiFi 密码清单 v" + Ui.Version;
            this.CaptionHeight = 48;
            this.ClientSize = new Size(Ui.Z(980), Ui.Z(700));
            this.MinimumSize = new Size(Ui.Z(860), Ui.Z(600));
            this.StartPosition = FormStartPosition.CenterScreen;

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
            _txtSearch.TextChanged += delegate { RebuildGrid(); };
            _searchHost.Controls.Add(_txtSearch);

            _chipKey = new ChipToggle();
            _chipKey.Text = "只看有密码";
            _chipKey.Toggled += delegate { RebuildGrid(); };
            this.Controls.Add(_chipKey);

            _btnAdd = Ui.MakeButton("添加 WiFi", false);
            _btnAdd.Click += OnAdd;
            this.Controls.Add(_btnAdd);

            _btnDelete = Ui.MakeButton("删除", false);
            _btnDelete.Click += OnDelete;
            this.Controls.Add(_btnDelete);

            _btnToggleAll = Ui.MakeButton("显示全部密码", false);
            _btnToggleAll.Click += delegate { SetAllRevealed(!_allShown); };
            this.Controls.Add(_btnToggleAll);

            _btnExport = Ui.MakeButton("导出 HTML 报告", true);
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
            c1.HeaderText = "网络名称";
            c1.FillWeight = 42F;
            c1.SortMode = DataGridViewColumnSortMode.NotSortable;
            _grid.Columns.Add(c1);

            DataGridViewTextBoxColumn c2 = new DataGridViewTextBoxColumn();
            c2.HeaderText = "安全类型";
            c2.FillWeight = 22F;
            c2.SortMode = DataGridViewColumnSortMode.NotSortable;
            _grid.Columns.Add(c2);

            DataGridViewTextBoxColumn c3 = new DataGridViewTextBoxColumn();
            c3.HeaderText = "连接方式";
            c3.FillWeight = 14F;
            c3.SortMode = DataGridViewColumnSortMode.NotSortable;
            _grid.Columns.Add(c3);

            DataGridViewTextBoxColumn c4 = new DataGridViewTextBoxColumn();
            c4.HeaderText = "密码";
            c4.FillWeight = 24F;
            c4.SortMode = DataGridViewColumnSortMode.NotSortable;
            _grid.Columns.Add(c4);
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
            _btnRefresh.SetBounds(bx - btnW, by, btnW, btnH);

            int barH = Ui.Z(30);

            int y = capH + Ui.Z(12);
            _stats.SetBounds(x, y, w, Ui.Z(84));
            y += Ui.Z(84) + Ui.Z(10);
            _current.SetBounds(x, y, w, Ui.Z(98));
            y += Ui.Z(98) + Ui.Z(12);

            int searchW = Ui.Z(250);
            _searchHost.SetBounds(x, y, searchW, barH);
            _txtSearch.SetBounds(Ui.Z(11), Math.Max(0, (barH - _txtSearch.PreferredHeight) / 2),
                searchW - Ui.Z(22), _txtSearch.PreferredHeight);

            int chipW = Ui.Z(108);
            _chipKey.SetBounds(x + searchW + Ui.Z(10), y, chipW, barH);

            int rx = x + w;
            int bw = Ui.Z(126);
            _btnExport.SetBounds(rx - bw, y, bw, barH); rx -= bw + Ui.Z(8);
            bw = Ui.Z(104);
            _btnToggleAll.SetBounds(rx - bw, y, bw, barH); rx -= bw + Ui.Z(8);
            bw = Ui.Z(62);
            _btnDelete.SetBounds(rx - bw, y, bw, barH); rx -= bw + Ui.Z(8);
            bw = Ui.Z(92);
            _btnAdd.SetBounds(rx - bw, y, bw, barH);

            y += barH + Ui.Z(10);

            int lineH = _lblStatus.Font.Height + Ui.Z(4);
            int statusY = this.ClientSize.Height - Ui.Z(8) - lineH;
            int gridH = statusY - Ui.Z(8) - y;
            if (gridH < Ui.Z(80)) gridH = Ui.Z(80);
            _grid.SetBounds(x, y, w, gridH);

            int hintW = Math.Min(Ui.Z(300), Math.Max(Ui.Z(120), w / 2));
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
            int textW = Math.Max(Ui.Z(120), this.ClientSize.Width - textX - Ui.Z(200));

            Ui.Text(g, "WiFi 密码清单", Ui.FTitle, Ui.Fg,
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
            _tips.SetToolTip(_btnTheme, dark ? "切换到浅色主题" : "切换到深色主题");
            _tips.SetToolTip(_btnMin, "最小化");
            _tips.SetToolTip(_btnClose, "关闭");
            _tips.SetToolTip(_btnRefresh, "重新读取（F5）");
            _tips.SetToolTip(_btnAdd, "添加一个 WiFi 配置");
            _tips.SetToolTip(_btnDelete, "删除所选网络的已保存配置");

            _searchHost.BackColor = Ui.Bg;
            _searchHost.Invalidate();
            _txtSearch.BackColor = Ui.Panel;
            _txtSearch.ForeColor = Ui.Fg;

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
            _lblStatus.Text = "正在读取已保存的 WiFi 配置…";
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
                snap.Error = error == null ? "读取失败" : error;
            }

            _snap = snap;
            _revealed.Clear();
            _allShown = false;
            _btnToggleAll.Text = "显示全部密码";

            _stats.Bind(_snap);
            _current.Bind(_snap);

            _sub = "v" + Ui.Version + "  ·  " + Environment.MachineName + "  ·  " + _snap.Nets.Count +
                " 个已保存网络  ·  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            this.Invalidate();

            RebuildGrid();

            if (_snap.Error != null && _snap.Error.Length > 0)
            {
                _lblStatus.Text = _snap.Error;
            }

            _busy = false;
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
                string sec = n.SecurityText();
                string mode = n.ModeLabel();
                string keyText;
                if (!n.HasKey)
                {
                    keyText = "无密码";
                }
                else if (_revealed.Contains(n.Ssid))
                {
                    keyText = n.Key.Length > 0 ? n.Key : "（未保存明文）";
                }
                else
                {
                    keyText = "\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022";
                }

                int idx = _grid.Rows.Add(name, sec, mode, keyText);
                DataGridViewRow row = _grid.Rows[idx];
                row.Tag = n;
                row.Cells[1].Style.ForeColor = Ui.TierColor(n.Tier());
                if (n.Current)
                {
                    row.Cells[0].Style.ForeColor = Ui.Accent;
                }
                if (!n.HasKey)
                {
                    row.Cells[3].Style.ForeColor = Ui.Mute;
                }
                else if (_revealed.Contains(n.Ssid))
                {
                    row.Cells[3].Style.ForeColor = Ui.Ok;
                    row.Cells[3].Style.Font = Ui.FVal;
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
                ? "没有读取到任何已保存的 WiFi 配置。"
                : "共 " + _snap.Nets.Count + " 个网络 · 当前显示 " + list.Count + " 个";
            _lblHint.Text = "双击显示密码 · 右键更多 · F5 刷新";

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
            _btnToggleAll.Text = on ? "隐藏全部密码" : "显示全部密码";
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

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.BackColor = Ui.PanelHi;
            menu.ForeColor = Ui.Fg;
            menu.ShowImageMargin = false;
            menu.Font = Ui.FSub;

            ToolStripMenuItem miName = new ToolStripMenuItem("复制网络名称");
            miName.Click += delegate { CopyText(n.Ssid); };
            menu.Items.Add(miName);

            if (n.HasKey)
            {
                ToolStripMenuItem miKey = new ToolStripMenuItem("复制密码");
                miKey.Enabled = n.Key.Length > 0;
                miKey.Click += delegate { CopyText(n.Key); };
                menu.Items.Add(miKey);

                ToolStripMenuItem miToggle = new ToolStripMenuItem(_revealed.Contains(n.Ssid) ? "隐藏密码" : "显示密码");
                miToggle.Click += delegate
                {
                    if (_revealed.Contains(n.Ssid)) _revealed.Remove(n.Ssid);
                    else _revealed.Add(n.Ssid);
                    RebuildGrid();
                };
                menu.Items.Add(miToggle);
            }

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem miDelete = new ToolStripMenuItem("删除此网络的配置");
            miDelete.ForeColor = Ui.Danger;
            miDelete.Click += delegate { DeleteEntry(n); };
            menu.Items.Add(miDelete);

            menu.Show(_grid, _grid.PointToClient(Cursor.Position));
        }

        private void CopyText(string text)
        {
            if (text == null || text.Length == 0) return;
            try
            {
                Clipboard.SetText(text);
                _lblStatus.Text = "已复制到剪贴板";
            }
            catch
            {
                _lblStatus.Text = "复制失败，剪贴板被其他程序占用";
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
                    WifiCollector.Connect(dlg.Ssid);
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
                    _lblStatus.Text = "已添加「" + dlg.Ssid + "」" + (dlg.ConnectNow ? "，正在尝试连接" : "");
                }
                else
                {
                    Prompt.Info(this, "添加失败",
                        FirstLines(r.Text, 8) + "\r\n\r\n" +
                        "常见原因：\r\n" +
                        "· 名称或密码不符合该安全类型的要求\r\n" +
                        "· 无线网卡被禁用\r\n" +
                        "· 需要以管理员身份运行本程序");
                }
            }
        }

        private void OnDelete(object sender, EventArgs e)
        {
            WifiEntry n = Selected();
            if (n == null)
            {
                Prompt.Info(this, "删除 WiFi", "请先在列表中选择要删除的网络。");
                return;
            }
            DeleteEntry(n);
        }

        private void DeleteEntry(WifiEntry n)
        {
            if (n == null || _busy) return;

            string msg = "确定要从这台电脑删除「" + n.Ssid + "」的已保存配置吗？\r\n\r\n删除后，再次连接需要重新输入密码。";
            if (n.Current)
            {
                msg += "\r\n\r\n注意：这是当前正在连接的网络，删除后连接会立即断开。";
            }

            if (!Prompt.Ask(this, "删除 WiFi", msg, "删除", true)) return;

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
                Prompt.Info(this, "删除失败",
                    FirstLines(r.Text, 8) + "\r\n\r\n" +
                    "若提示权限不足，请右键 WifiList.exe 以管理员身份运行后重试。");
            }
            else
            {
                _lblStatus.Text = "已删除「" + n.Ssid + "」的配置";
            }
        }

        private static string FirstLines(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "（netsh 没有返回详细信息）";
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            List<string> keep = new List<string>();
            for (int i = 0; i < lines.Length; i++)
            {
                string s = lines[i].Trim();
                if (s.Length == 0) continue;
                keep.Add(s);
                if (keep.Count >= max) break;
            }
            return keep.Count == 0 ? "（netsh 没有返回详细信息）" : string.Join("\r\n", keep.ToArray());
        }

        private void OnExport(object sender, EventArgs e)
        {
            if (_snap == null || _snap.Nets.Count == 0) return;
            try
            {
                string path = HtmlExporter.DefaultPath();
                HtmlExporter.Export(path, _snap);
                _lblStatus.Text = "已导出：" + path;
                HtmlExporter.OpenWithShell(path);
            }
            catch (Exception ex)
            {
                Prompt.Info(this, "导出失败", ex.Message);
            }
        }

        // ------------------------------------------------------------------
        // misc
        // ------------------------------------------------------------------

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
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
                SendMessage(_txtSearch.Handle, 0x1501, new IntPtr(1), "搜索 WiFi 名称…");
            }
            catch { }
        }
    }
}
