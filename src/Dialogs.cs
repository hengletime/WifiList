using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace WifiList
{
    /// <summary>Rounded, themed text input host (a borderless TextBox inside a painted frame).</summary>
    internal sealed class InputHost : Panel
    {
        public readonly TextBox Box;

        public InputHost()
        {
            this.DoubleBuffered = true;
            this.BackColor = Ui.Bg;
            this.Box = new TextBox();
            this.Box.BorderStyle = BorderStyle.None;
            this.Box.Font = Ui.FSub;
            this.Controls.Add(this.Box);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int bh = this.Box.PreferredHeight;
            this.Box.SetBounds(Ui.Z(12), (this.Height - bh) / 2, Math.Max(Ui.Z(10), this.Width - Ui.Z(24)), bh);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Ui.FillRound(e.Graphics, new Rectangle(0, 0, this.Width - 1, this.Height - 1), Ui.Z(8), Ui.Panel, Ui.Line);
        }

        public void ApplyTheme()
        {
            this.BackColor = Ui.Bg;
            this.Box.BackColor = Ui.Panel;
            this.Box.ForeColor = Ui.Fg;
            this.Invalidate();
        }
    }

    /// <summary>
    /// Themed drop down list. A popup Form would fight with focus, so this is a
    /// plain panel that the owning dialog floats above its controls.
    /// </summary>
    internal sealed class DropList : Control
    {
        private string[] _items = new string[0];
        private int _itemH = 32;
        private int _hover = -1;
        private int _sel = -1;

        public int PickedIndex = -1;
        public event EventHandler Picked;

        public DropList()
        {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                          ControlStyles.OptimizedDoubleBuffer, true);
            this.Cursor = Cursors.Hand;
            this.Visible = false;
        }

        public void ShowList(Rectangle bounds, string[] items, int itemH, int selected)
        {
            _items = items == null ? new string[0] : items;
            _itemH = Math.Max(1, itemH);
            _sel = selected;
            _hover = -1;
            this.Bounds = bounds;
            this.Visible = true;
            this.Invalidate();
        }

        private int IndexAt(int y)
        {
            int pad = Ui.Z(4);
            int i = (y - pad) / _itemH;
            if (i < 0 || i >= _items.Length) return -1;
            return i;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = IndexAt(e.Y);
            if (i != _hover)
            {
                _hover = i;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != -1)
            {
                _hover = -1;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int i = IndexAt(e.Y);
            if (i < 0) return;
            PickedIndex = i;
            _sel = i;
            this.Visible = false;
            if (Picked != null) Picked(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            Ui.FillRound(g, new Rectangle(0, 0, this.Width - 1, this.Height - 1), Ui.Z(8), Ui.PanelHi, Ui.LineHi);

            int pad = Ui.Z(4);
            int innerW = this.Width - pad * 2;
            for (int i = 0; i < _items.Length; i++)
            {
                Rectangle row = new Rectangle(pad, pad + i * _itemH, innerW, _itemH);
                if (i == _hover)
                {
                    Ui.FillRound(g, row, Ui.Z(6),
                        Ui.Mix(Ui.Accent, Ui.PanelHi, 0.78));
                }
                bool sel = (i == _sel);
                Color ink = sel ? Ui.Accent : Ui.Fg;
                Ui.Text(g, _items[i], Ui.FSub, ink,
                    new Rectangle(row.X + Ui.Z(10), row.Y, row.Width - Ui.Z(34), row.Height), Ui.LeftMid);

                if (sel)
                {
                    float k = Ui.ZF(1F);
                    using (Pen pen = new Pen(Ui.Accent, 1.7F * k))
                    {
                        pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                        pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                        pen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                        float cx = row.Right - Ui.ZF(16);
                        float cy = row.Y + row.Height / 2F;
                        g.DrawLines(pen, new PointF[]
                        {
                            new PointF(cx - 5F * k, cy),
                            new PointF(cx - 1F * k, cy + 4F * k),
                            new PointF(cx + 6F * k, cy - 4F * k)
                        });
                    }
                }
            }
        }
    }

    /// <summary>Themed message / confirmation dialog.</summary>
    internal sealed class MessageForm : ChromeForm
    {
        private const int Pad = 22;

        private readonly string _title;
        private readonly string _message;
        private readonly string _okText;
        private readonly bool _ask;
        private readonly bool _danger;
        private readonly int _textH;

        private Button _btnOk;
        private Button _btnCancel;
        private GlyphButton _btnClose;

        public MessageForm(string title, string message, string okText, bool ask, bool danger)
        {
            _title = title == null ? "" : title;
            _message = message == null ? "" : message;
            _okText = okText == null ? L.T("btn.confirm") : okText;
            _ask = ask;
            _danger = danger;

            this.CaptionHeight = 42;
            this.AllowResize = false;
            this.Text = _title;
            this.KeyPreview = true;
            this.StartPosition = FormStartPosition.CenterParent;

            int wClient = Ui.Z(440);
            int textW = wClient - Ui.Z(Pad) * 2;
            Size measured = TextRenderer.MeasureText(_message, Ui.FSub, new Size(textW, Ui.Z(2000)),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            _textH = Math.Max(Ui.Z(20), measured.Height);

            int h = CapH + Ui.Z(14) + _textH + Ui.Z(16) + Ui.Z(30) + Ui.Z(20);
            this.ClientSize = new Size(wClient, h);
            this.MinimumSize = this.ClientSize;
            this.MaximumSize = this.ClientSize;

            _btnClose = new GlyphButton(GlyphKind.Close);
            _btnClose.DangerHover = true;
            _btnClose.Click += delegate
            {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            };
            this.Controls.Add(_btnClose);

            _btnOk = Ui.MakeButton(_okText, true);
            if (_danger)
            {
                _btnOk.BackColor = Ui.Danger;
                _btnOk.ForeColor = Color.White;
                _btnOk.FlatAppearance.MouseOverBackColor = Ui.Mix(Ui.Danger, Color.White, 0.14);
                _btnOk.FlatAppearance.MouseDownBackColor = Ui.Mix(Ui.Danger, Color.Black, 0.18);
            }
            _btnOk.Click += delegate
            {
                this.DialogResult = DialogResult.OK;
                this.Close();
            };
            this.Controls.Add(_btnOk);
            this.AcceptButton = _btnOk;

            if (_ask)
            {
                _btnCancel = Ui.MakeButton(L.T("btn.cancel"), false);
                _btnCancel.Click += delegate
                {
                    this.DialogResult = DialogResult.Cancel;
                    this.Close();
                };
                this.Controls.Add(_btnCancel);
                this.CancelButton = _btnCancel;
            }

            LayoutChildren();
        }

        private void LayoutChildren()
        {
            int pad = Ui.Z(Pad);
            int cw = Ui.Z(30);
            int ch = Ui.Z(26);
            _btnClose.SetBounds(this.ClientSize.Width - pad - cw, (CapH - ch) / 2, cw, ch);

            int btnH = Ui.Z(30);
            int btnY = this.ClientSize.Height - Ui.Z(20) - btnH;
            int rx = this.ClientSize.Width - pad;
            int wOk = Math.Max(Ui.Z(88), Ui.TextW(_okText, Ui.FBtnBold) + Ui.Z(28));
            _btnOk.SetBounds(rx - wOk, btnY, wOk, btnH);
            rx -= wOk + Ui.Z(8);
            if (_btnCancel != null)
            {
                int wCancel = Math.Max(Ui.Z(76), Ui.TextW(_btnCancel.Text, Ui.FBtn) + Ui.Z(28));
                _btnCancel.SetBounds(rx - wCancel, btnY, wCancel, btnH);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            int pad = Ui.Z(Pad);

            Ui.Text(g, _title, Ui.FTitle, Ui.Fg,
                new Rectangle(pad, Ui.Z(9), this.ClientSize.Width - pad * 2 - Ui.Z(40), Ui.FTitle.Height + Ui.Z(2)),
                Ui.LeftTop);

            using (Pen pen = new Pen(Ui.Line, 1F))
            {
                g.DrawLine(pen, pad, CapH - 1, this.ClientSize.Width - pad, CapH - 1);
            }

            TextRenderer.DrawText(g, _message, Ui.FSub,
                new Rectangle(pad, CapH + Ui.Z(14), this.ClientSize.Width - pad * 2, _textH),
                Ui.Dim, TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
    }

    internal static class Prompt
    {
        public static void Info(IWin32Window owner, string title, string message)
        {
            using (MessageForm f = new MessageForm(title, message, L.T("btn.ok"), false, false))
            {
                f.ShowDialog(owner);
            }
        }

        public static bool Ask(IWin32Window owner, string title, string message, string okText, bool danger)
        {
            using (MessageForm f = new MessageForm(title, message, okText, true, danger))
            {
                f.ShowDialog(owner);
                return f.DialogResult == DialogResult.OK;
            }
        }
    }

    /// <summary>New wireless profile: SSID, security type, passphrase, options.</summary>
    internal sealed class AddWifiForm : ChromeForm
    {
        private const int Pad = 22;

        // selected values, read by the caller after DialogResult.OK
        public string Ssid = "";
        public string Auth = "WPA2PSK";
        public string Enc = "AES";
        public string Key = "";
        public bool HiddenNet = false;
        public bool AutoMode = true;
        public bool ConnectNow = true;

        private static readonly string[] AuthCodes = new string[] { "WPA2PSK", "WPA3SAE", "WPAPSK", "open" };
        private static readonly string[] EncCodes = new string[] { "AES", "AES", "TKIP", "none" };
        private static string[] AuthNames()
        {
            return new string[]
            {
                L.T("d.auth0"), L.T("d.auth1"), L.T("d.auth2"), L.T("d.auth3")
            };
        }

        private int _authIndex = 0;

        private InputHost _hostSsid;
        private InputHost _hostKey;
        private Button _btnAuth;
        private Button _btnShow;
        private Button _btnSave;
        private Button _btnCancel;
        private ChipToggle _chipHidden;
        private ChipToggle _chipAuto;
        private ChipToggle _chipConnect;
        private GlyphButton _btnClose;
        private DropList _drop;

        // label geometry is computed once in LayoutChildren and reused by the
        // painter, so a label can never drift away from its control again
        private Rectangle _lblSsid;
        private Rectangle _lblAuth;
        private Rectangle _lblKey;
        private string _lblKeyText = L.T("d.key");

        private string _title = L.T("d.addTitle");


        public AddWifiForm()
        {
            this.CaptionHeight = 42;
            this.AllowResize = false;
            this.Text = L.T("d.addTitle");
            this.KeyPreview = true;
            this.StartPosition = FormStartPosition.CenterParent;
            this.ClientSize = new Size(Ui.Z(470), Ui.Z(368));
            this.MinimumSize = this.ClientSize;
            this.MaximumSize = this.ClientSize;

            _btnClose = new GlyphButton(GlyphKind.Close);
            _btnClose.DangerHover = true;
            _btnClose.Click += delegate { this.DialogResult = DialogResult.Cancel; this.Close(); };
            this.Controls.Add(_btnClose);

            _hostSsid = new InputHost();
            _hostSsid.Box.Enter += delegate { CloseDrop(); };
            this.Controls.Add(_hostSsid);

            _btnAuth = Ui.MakeButton("", false);
            _btnAuth.TextAlign = ContentAlignment.MiddleLeft;
            _btnAuth.Padding = new Padding(Ui.Z(12), 0, 0, 0);
            _btnAuth.Click += delegate { OpenDrop(); };
            this.Controls.Add(_btnAuth);

            _hostKey = new InputHost();
            _hostKey.Box.PasswordChar = '\u25CF';
            _hostKey.Box.MaxLength = 63;
            _hostKey.Box.Enter += delegate { CloseDrop(); };
            this.Controls.Add(_hostKey);

            _btnShow = Ui.MakeButton(L.T("d.showKey"), false);
            _btnShow.Click += delegate { ToggleKeyVisible(); };
            this.Controls.Add(_btnShow);

            _chipHidden = new ChipToggle();
            _chipHidden.Text = L.T("d.chipHidden");
            _chipHidden.Toggled += delegate { CloseDrop(); };
            this.Controls.Add(_chipHidden);

            _chipAuto = new ChipToggle();
            _chipAuto.Text = L.T("d.chipAuto");
            _chipAuto.On = true;
            _chipAuto.Toggled += delegate { CloseDrop(); };
            this.Controls.Add(_chipAuto);

            _chipConnect = new ChipToggle();
            _chipConnect.Text = L.T("d.chipConnect");
            _chipConnect.On = true;
            _chipConnect.Toggled += delegate { CloseDrop(); };
            this.Controls.Add(_chipConnect);

            _btnSave = Ui.MakeButton(L.T("d.save"), true);
            _btnSave.Click += OnSave;
            this.Controls.Add(_btnSave);
            this.AcceptButton = _btnSave;

            _btnCancel = Ui.MakeButton(L.T("btn.cancel"), false);
            _btnCancel.Click += delegate { this.DialogResult = DialogResult.Cancel; this.Close(); };
            this.Controls.Add(_btnCancel);
            this.CancelButton = _btnCancel;

            _drop = new DropList();
            _drop.Picked += delegate
            {
                int i = _drop.PickedIndex;
                if (i >= 0 && i < AuthNames().Length)
                {
                    _authIndex = i;
                    UpdateAuthUi();
                }
            };
            this.Controls.Add(_drop);

            UpdateAuthUi();

            // the three chips must fit side by side in every language
            int chips = Ui.TextW(L.T("d.chipHidden"), Ui.FSub) + Ui.TextW(L.T("d.chipAuto"), Ui.FSub) +
                Ui.TextW(L.T("d.chipConnect"), Ui.FSub) + Ui.Z(44) * 3 + Ui.Z(16);
            int needW = chips + Ui.Z(Pad) * 2;
            if (needW > this.ClientSize.Width)
            {
                this.ClientSize = new Size(needW, this.ClientSize.Height);
                this.MinimumSize = this.ClientSize;
                this.MaximumSize = this.ClientSize;
            }

            ApplyTheme();
            LayoutChildren();
        }

        /// <summary>
        /// Edit mode: same form, pre-filled from an existing profile. The
        /// caller re-adds the profile with the new values; see MainForm.
        /// </summary>
        public AddWifiForm(WifiEntry existing)
            : this()
        {
            _title = L.T("d.editTitle");
            this.Text = L.T("d.editTitle");

            _chipHidden.On = existing.Hidden;
            _chipAuto.On = string.Equals(existing.Mode, "auto", StringComparison.OrdinalIgnoreCase);
            _chipConnect.On = false;

            _authIndex = IndexOfAuth(existing.AuthCode);
            UpdateAuthUi();

            _hostSsid.Box.Text = existing.Ssid;
            _hostKey.Box.Text = existing.Key;
            _btnSave.Text = L.T("d.saveEdit");

            this.Invalidate();
        }

        private static int IndexOfAuth(string code)
        {
            for (int i = 0; i < AuthCodes.Length; i++)
            {
                if (string.Equals(AuthCodes[i], code, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return 0;
        }

        private void OpenDrop()
        {
            if (_drop == null) return;
            int itemH = Ui.Z(32);
            string[] names = AuthNames();
            Rectangle box = new Rectangle(_btnAuth.Left, _btnAuth.Bottom + Ui.Z(3), _btnAuth.Width,
                itemH * names.Length + Ui.Z(8));
            _drop.ShowList(box, names, itemH, _authIndex);
            _drop.BringToFront();
        }

        private void CloseDrop()
        {
            if (_drop != null) _drop.Visible = false;
        }

        protected override void OnDeactivate(EventArgs e)
        {
            CloseDrop();
            base.OnDeactivate(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape && _drop != null && _drop.Visible)
            {
                CloseDrop();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void UpdateAuthUi()
        {
            _btnAuth.Text = AuthNames()[_authIndex];
            _btnAuth.Invalidate();

            bool open = AuthCodes[_authIndex] == "open";
            _hostKey.Box.Enabled = !open;
            _hostKey.Box.TabStop = !open;
            _btnShow.Enabled = !open;
            _hostKey.Box.PasswordChar = open ? '\0' : '\u25CF';

            if (open)
            {
                _hostKey.Box.Clear();
                _btnShow.Text = L.T("d.showKey");
            }
            _lblKeyText = open ? L.T("d.keyOpen") : L.T("d.key");

            Ui.StyleButton(_btnShow, false);
            if (open) _btnShow.ForeColor = Ui.Mute;
            this.Invalidate();
        }

        private void ToggleKeyVisible()
        {
            bool showing = _hostKey.Box.PasswordChar == '\0';
            _hostKey.Box.PasswordChar = showing ? '\u25CF' : '\0';
            _btnShow.Text = showing ? L.T("d.showKey") : L.T("d.hideKey");
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            this.ActiveControl = _hostSsid.Box;
            _hostSsid.Box.Focus();
        }

        private void ApplyTheme()
        {
            Ui.StyleButton(_btnAuth, false);
            _btnAuth.ForeColor = Ui.Fg;
            Ui.StyleButton(_btnSave, true);
            Ui.StyleButton(_btnCancel, false);
            Ui.StyleButton(_btnShow, false);

            _hostSsid.ApplyTheme();
            _hostKey.ApplyTheme();

            _chipHidden.BackColor = Ui.Bg;
            _chipAuto.BackColor = Ui.Bg;
            _chipConnect.BackColor = Ui.Bg;
            _drop.BackColor = Ui.PanelHi;
            this.RefreshChrome();
        }

        private void LayoutChildren()
        {
            int pad = Ui.Z(Pad);
            int w = this.ClientSize.Width - pad * 2;
            int labelH = Ui.FKey.Height + Ui.Z(2);
            int boxH = Ui.Z(34);

            int cw = Ui.Z(30);
            int ch = Ui.Z(26);
            _btnClose.SetBounds(this.ClientSize.Width - pad - cw, (CapH - ch) / 2, cw, ch);

            int y = CapH + Ui.Z(16);

            _lblSsid = new Rectangle(pad, y, w, labelH);
            y += labelH + Ui.Z(4);
            _hostSsid.SetBounds(pad, y, w, boxH);
            y += boxH + Ui.Z(14);

            _lblAuth = new Rectangle(pad, y, w, labelH);
            y += labelH + Ui.Z(4);
            _btnAuth.SetBounds(pad, y, w, boxH);
            y += boxH + Ui.Z(14);

            _lblKey = new Rectangle(pad, y, w, labelH);
            y += labelH + Ui.Z(4);
            int showW = Ui.Z(72);
            _hostKey.SetBounds(pad, y, w - showW - Ui.Z(8), boxH);
            _btnShow.SetBounds(pad + w - showW, y + Ui.Z(4), showW, boxH - Ui.Z(8));
            y += boxH + Ui.Z(16);

            int chipH = Ui.Z(30);
            int cx = pad;
            int wHidden = Math.Max(Ui.Z(104), Ui.TextW(_chipHidden.Text, Ui.FSub) + Ui.Z(44));
            int wAuto = Math.Max(Ui.Z(104), Ui.TextW(_chipAuto.Text, Ui.FSub) + Ui.Z(44));
            int wConnect = Math.Max(Ui.Z(118), Ui.TextW(_chipConnect.Text, Ui.FSub) + Ui.Z(44));
            _chipHidden.SetBounds(cx, y, wHidden, chipH); cx += wHidden + Ui.Z(8);
            _chipAuto.SetBounds(cx, y, wAuto, chipH); cx += wAuto + Ui.Z(8);
            _chipConnect.SetBounds(cx, y, wConnect, chipH);

            int btnH = Ui.Z(30);
            int btnY = this.ClientSize.Height - Ui.Z(20) - btnH;
            int rx = this.ClientSize.Width - pad;
            int wSave = Math.Max(Ui.Z(84), Ui.TextW(_btnSave.Text, Ui.FBtnBold) + Ui.Z(28));
            int wCancel = Math.Max(Ui.Z(76), Ui.TextW(_btnCancel.Text, Ui.FBtn) + Ui.Z(28));
            _btnSave.SetBounds(rx - wSave, btnY, wSave, btnH);
            _btnCancel.SetBounds(rx - wSave - Ui.Z(8) - wCancel, btnY, wCancel, btnH);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            int pad = Ui.Z(Pad);

            Ui.Text(g, _title, Ui.FTitle, Ui.Fg,
                new Rectangle(pad, Ui.Z(9), this.ClientSize.Width - pad * 2 - Ui.Z(40), Ui.FTitle.Height + Ui.Z(2)),
                Ui.LeftTop);

            using (Pen pen = new Pen(Ui.Line, 1F))
            {
                g.DrawLine(pen, pad, CapH - 1, this.ClientSize.Width - pad, CapH - 1);
            }

            Ui.Text(g, L.T("d.ssid"), Ui.FKey, Ui.Mute, _lblSsid, Ui.LeftTop);
            Ui.Text(g, L.T("d.security"), Ui.FKey, Ui.Mute, _lblAuth, Ui.LeftTop);
            Ui.Text(g, _lblKeyText, Ui.FKey, Ui.Mute, _lblKey, Ui.LeftTop);
        }

        private void OnSave(object sender, EventArgs e)
        {
            string ssid = _hostSsid.Box.Text.Trim();
            if (ssid.Length == 0)
            {
                Prompt.Info(this, L.T("d.cannotSave"), L.T("d.errNoSsid"));
                _hostSsid.Box.Focus();
                return;
            }
            if (System.Text.Encoding.UTF8.GetByteCount(ssid) > 32)
            {
                Prompt.Info(this, L.T("d.cannotSave"), L.T("d.errSsidLong"));
                _hostSsid.Box.Focus();
                return;
            }

            bool open = AuthCodes[_authIndex] == "open";
            string key = _hostKey.Box.Text;

            if (!open)
            {
                if (key.Length < 8 || key.Length > 63)
                {
                    Prompt.Info(this, L.T("d.cannotSave"), L.F("d.errKeyLen", key.Length));
                    _hostKey.Box.Focus();
                    return;
                }
            }

            this.Ssid = ssid;
            this.Auth = AuthCodes[_authIndex];
            this.Enc = EncCodes[_authIndex];
            this.Key = open ? "" : key;
            this.HiddenNet = _chipHidden.On;
            this.AutoMode = _chipAuto.On;
            this.ConnectNow = _chipConnect.On;

            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }

    /// <summary>About box: identity, runtime facts and the privacy note.</summary>
    internal sealed class AboutForm : ChromeForm
    {
        private const int Pad = 22;
        private const string HomePage = "https://github.com/hengletime/WifiList";

        private enum UpdState
        {
            Checking,
            Latest,
            Newer,
            Failed
        }

        private readonly WifiSnapshot _snap;
        private readonly int _tagH;
        private readonly int _privH;
        private readonly int _rowsY;
        private readonly int _updY;

        private Button _btnClose;
        private Button _btnHome;
        private Button _btnCopy;
        private Button _btnUpd;
        private GlyphButton _btnX;

        private UpdState _updState = UpdState.Checking;
        private string _updText = "";
        private string _updUrl = "";
        private bool _checking;

        public AboutForm(WifiSnapshot snap)
        {
            _snap = snap;

            this.CaptionHeight = 42;
            this.AllowResize = false;
            this.Text = L.T("ab.title");
            this.KeyPreview = true;
            this.StartPosition = FormStartPosition.CenterParent;

            int wClient = Ui.Z(516);
            int textW = wClient - Ui.Z(Pad) * 2;

            _tagH = Measure(L.T("ab.tagline"), textW);
            _privH = Measure(L.T("ab.privacy"), textW);

            int updH = Ui.Z(26);
            int head = CapH + Ui.Z(16) + Ui.Z(46) + Ui.Z(12);
            _rowsY = head + _tagH + Ui.Z(16);
            int rowH = Ui.FVal.Height + Ui.Z(7);
            _updY = _rowsY + rowH * 5 + Ui.Z(14);
            int h = _updY + updH + Ui.Z(12) + _privH + Ui.Z(16) + Ui.Z(30) + Ui.Z(20);

            this.ClientSize = new Size(wClient, h);
            this.MinimumSize = this.ClientSize;
            this.MaximumSize = this.ClientSize;

            _btnX = new GlyphButton(GlyphKind.Close);
            _btnX.DangerHover = true;
            _btnX.Click += delegate { this.Close(); };
            this.Controls.Add(_btnX);

            _btnCopy = Ui.MakeButton(L.T("ab.copy"), false);
            _btnCopy.Click += delegate { CopyInfo(); };
            this.Controls.Add(_btnCopy);

            _btnUpd = Ui.MakeButton(L.T("ab.check"), false);
            _btnUpd.Click += delegate { OnUpdateClick(); };
            this.Controls.Add(_btnUpd);

            _btnHome = Ui.MakeButton(L.T("ab.openHome"), false);
            _btnHome.Click += delegate { OpenHome(); };
            this.Controls.Add(_btnHome);

            _btnClose = Ui.MakeButton(L.T("ab.close"), true);
            _btnClose.Click += delegate { this.Close(); };
            this.Controls.Add(_btnClose);
            this.AcceptButton = _btnClose;
            this.CancelButton = _btnClose;

            LayoutChildren();
        }

        private static int Measure(string text, int width)
        {
            Size sz = TextRenderer.MeasureText(text, Ui.FSub, new Size(width, Ui.Z(2000)),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            return Math.Max(Ui.Z(18), sz.Height);
        }

        private void LayoutChildren()
        {
            int pad = Ui.Z(Pad);
            int cw = Ui.Z(30);
            int ch = Ui.Z(26);
            _btnX.SetBounds(this.ClientSize.Width - pad - cw, (CapH - ch) / 2, cw, ch);

            int btnH = Ui.Z(30);
            int btnY = this.ClientSize.Height - Ui.Z(20) - btnH;
            int rx = this.ClientSize.Width - pad;

            int wClose = Math.Max(Ui.Z(84), Ui.TextW(_btnClose.Text, Ui.FBtnBold) + Ui.Z(28));
            int wHome = Math.Max(Ui.Z(126), Ui.TextW(_btnHome.Text, Ui.FBtn) + Ui.Z(28));
            int wCopy = Math.Max(Ui.Z(122), Ui.TextW(L.T("ab.copy"), Ui.FBtn) + Ui.Z(28));
            _btnClose.SetBounds(rx - wClose, btnY, wClose, btnH);
            rx -= wClose + Ui.Z(8);
            _btnHome.SetBounds(rx - wHome, btnY, wHome, btnH);
            rx -= wHome + Ui.Z(8);
            _btnCopy.SetBounds(pad, btnY, wCopy, btnH);

            int wUpd = Math.Max(Ui.Z(96), Ui.TextW(_btnUpd.Text, Ui.FBtn) + Ui.Z(26));
            _btnUpd.SetBounds(this.ClientSize.Width - pad - wUpd, _updY - Ui.Z(4), wUpd, Ui.Z(26));
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            StartCheck();
        }

        /// <summary>Runs the network lookup off the UI thread: opening About must never block.</summary>
        private void StartCheck()
        {
            if (_checking) return;

            _checking = true;
            _updState = UpdState.Checking;
            _updText = L.T("ab.checking");
            _updUrl = "";
            _btnUpd.Text = L.T("ab.check");
            _btnUpd.Enabled = false;
            LayoutChildren();
            this.Invalidate();

            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                ReleaseInfo info = UpdateCheck.Latest(6000);
                try
                {
                    this.BeginInvoke((MethodInvoker)delegate { ApplyRelease(info); });
                }
                catch { }
            });
        }

        private void ApplyRelease(ReleaseInfo info)
        {
            _checking = false;
            if (this.IsDisposed) return;

            if (info == null)
            {
                _updState = UpdState.Failed;
                _updText = L.T("ab.checkFailed");
                _btnUpd.Text = L.T("ab.recheck");
                _btnUpd.Enabled = true;
                _updUrl = "";
            }
            else if (UpdateCheck.IsNewer(info.Version))
            {
                _updState = UpdState.Newer;
                _updText = L.F("ab.newVersion", "v" + info.Version.ToString(3));
                _btnUpd.Text = L.T("ab.download");
                _btnUpd.Enabled = true;
                _updUrl = info.Url.Length > 0 ? info.Url : UpdateCheck.ReleasesPage;
            }
            else
            {
                _updState = UpdState.Latest;
                _updText = L.F("ab.upToDate", "v" + Ui.Version);
                _btnUpd.Text = L.T("ab.recheck");
                _btnUpd.Enabled = true;
                _updUrl = "";
            }

            LayoutChildren();
            this.Invalidate();
        }

        private void OnUpdateClick()
        {
            if (_updState == UpdState.Newer)
            {
                try { Process.Start(_updUrl); } catch { }
                return;
            }
            StartCheck();
        }

        private void CopyInfo()
        {
            try
            {
                Clipboard.SetText(L.T("app.title") + " v" + Ui.Version + "\r\n" + HomePage +
                    "\r\n" + Environment.OSVersion.VersionString + "  ·  .NET " + Environment.Version +
                    "\r\n" + Environment.MachineName);
                _btnCopy.Text = L.T("ab.copied");
            }
            catch { }
        }

        private void OpenHome()
        {
            try
            {
                Process.Start(HomePage);
            }
            catch { }
        }

        private void Row(Graphics g, int pad, int w, ref int y, string key, string val)
        {
            int kw = Ui.Z(104);
            int h = Ui.FVal.Height + Ui.Z(2);
            Ui.Text(g, key, Ui.FKey, Ui.Mute, new Rectangle(pad, y, kw - Ui.Z(8), h), Ui.LeftTop);
            Ui.Text(g, val, Ui.FVal, Ui.Dim, new Rectangle(pad + kw, y, w - kw, h), Ui.LeftTop);
            y += h + Ui.Z(5);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            int pad = Ui.Z(Pad);
            int w = this.ClientSize.Width - pad * 2;

            Ui.Text(g, L.T("ab.title"), Ui.FTitle, Ui.Fg,
                new Rectangle(pad, Ui.Z(9), w - Ui.Z(40), Ui.FTitle.Height + Ui.Z(2)), Ui.LeftTop);
            using (Pen pen = new Pen(Ui.Line, 1F))
            {
                g.DrawLine(pen, pad, CapH - 1, this.ClientSize.Width - pad, CapH - 1);
            }

            int y = CapH + Ui.Z(16);
            int logo = Ui.Z(46);
            Ui.DrawLogo(g, new Rectangle(pad, y, logo, logo));

            int tx = pad + logo + Ui.Z(14);
            int tw = w - logo - Ui.Z(14);
            Ui.Text(g, L.T("app.title"), Ui.FTitle, Ui.Fg,
                new Rectangle(tx, y + Ui.Z(1), tw, Ui.FTitle.Height + Ui.Z(2)), Ui.LeftTop);
            Ui.Text(g, "v" + Ui.Version, Ui.FSub, Ui.Mute,
                new Rectangle(tx, y + Ui.Z(4) + Ui.FTitle.Height, tw, Ui.FSub.Height + Ui.Z(2)), Ui.LeftTop);

            y += logo + Ui.Z(12);
            TextRenderer.DrawText(g, L.T("ab.tagline"), Ui.FSub, new Rectangle(pad, y, w, _tagH),
                Ui.Dim, TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

            y = _rowsY;
            Row(g, pad, w, ref y, L.T("ab.version"), "v" + Ui.Version);
            Row(g, pad, w, ref y, L.T("ab.host"), Environment.MachineName);
            Row(g, pad, w, ref y, L.T("ab.os"), Environment.OSVersion.VersionString);
            Row(g, pad, w, ref y, L.T("ab.clr"), ".NET " + Environment.Version);
            Row(g, pad, w, ref y, L.T("ab.nets"),
                _snap == null ? "-" : _snap.Nets.Count.ToString());

            y = _updY;
            Color updInk = _updState == UpdState.Newer ? Ui.Accent : Ui.Mute;
            if (_updState == UpdState.Latest) updInk = Ui.Dim;
            Ui.Text(g, _updText, Ui.FSub, updInk,
                new Rectangle(pad, y + Ui.Z(4), Math.Max(Ui.Z(80), w - Ui.Z(112)), Ui.FSub.Height + Ui.Z(2)),
                Ui.LeftTop);

            y += Ui.Z(26) + Ui.Z(12);
            TextRenderer.DrawText(g, L.T("ab.privacy"), Ui.FSub, new Rectangle(pad, y, w, _privH),
                Ui.Mute, TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
    }
}
