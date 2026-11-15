using System;
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
            _okText = okText == null ? "确定" : okText;
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
                _btnCancel = Ui.MakeButton("取消", false);
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
            _btnOk.SetBounds(rx - Ui.Z(88), btnY, Ui.Z(88), btnH);
            rx -= Ui.Z(88) + Ui.Z(8);
            if (_btnCancel != null)
            {
                _btnCancel.SetBounds(rx - Ui.Z(76), btnY, Ui.Z(76), btnH);
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
            using (MessageForm f = new MessageForm(title, message, "知道了", false, false))
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
        private static readonly string[] AuthNames = new string[]
        {
            "WPA2-PSK（AES）· 最常用",
            "WPA3-SAE（AES）· 更安全",
            "WPA-PSK（TKIP）· 兼容老设备",
            "开放网络（无密码）"
        };

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
        private string _lblKeyText = "密码（8–63 位）";

        public AddWifiForm()
        {
            this.CaptionHeight = 42;
            this.AllowResize = false;
            this.Text = "添加 WiFi";
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

            _btnShow = Ui.MakeButton("显示密码", false);
            _btnShow.Click += delegate { ToggleKeyVisible(); };
            this.Controls.Add(_btnShow);

            _chipHidden = new ChipToggle();
            _chipHidden.Text = "隐藏网络";
            _chipHidden.Toggled += delegate { CloseDrop(); };
            this.Controls.Add(_chipHidden);

            _chipAuto = new ChipToggle();
            _chipAuto.Text = "自动连接";
            _chipAuto.On = true;
            _chipAuto.Toggled += delegate { CloseDrop(); };
            this.Controls.Add(_chipAuto);

            _chipConnect = new ChipToggle();
            _chipConnect.Text = "保存后连接";
            _chipConnect.On = true;
            _chipConnect.Toggled += delegate { CloseDrop(); };
            this.Controls.Add(_chipConnect);

            _btnSave = Ui.MakeButton("保存", true);
            _btnSave.Click += OnSave;
            this.Controls.Add(_btnSave);
            this.AcceptButton = _btnSave;

            _btnCancel = Ui.MakeButton("取消", false);
            _btnCancel.Click += delegate { this.DialogResult = DialogResult.Cancel; this.Close(); };
            this.Controls.Add(_btnCancel);
            this.CancelButton = _btnCancel;

            _drop = new DropList();
            _drop.Picked += delegate
            {
                int i = _drop.PickedIndex;
                if (i >= 0 && i < AuthNames.Length)
                {
                    _authIndex = i;
                    UpdateAuthUi();
                }
            };
            this.Controls.Add(_drop);

            UpdateAuthUi();
            ApplyTheme();
            LayoutChildren();
        }

        private void OpenDrop()
        {
            if (_drop == null) return;
            int itemH = Ui.Z(32);
            Rectangle box = new Rectangle(_btnAuth.Left, _btnAuth.Bottom + Ui.Z(3), _btnAuth.Width,
                itemH * AuthNames.Length + Ui.Z(8));
            _drop.ShowList(box, AuthNames, itemH, _authIndex);
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
            _btnAuth.Text = AuthNames[_authIndex];
            _btnAuth.Invalidate();

            bool open = AuthCodes[_authIndex] == "open";
            _hostKey.Box.Enabled = !open;
            _hostKey.Box.TabStop = !open;
            _btnShow.Enabled = !open;
            _hostKey.Box.PasswordChar = open ? '\0' : '\u25CF';

            if (open)
            {
                _hostKey.Box.Clear();
                _btnShow.Text = "显示密码";
            }
            _lblKeyText = open ? "密码（开放网络无需密码）" : "密码（8–63 位）";

            Ui.StyleButton(_btnShow, false);
            if (open) _btnShow.ForeColor = Ui.Mute;
            this.Invalidate();
        }

        private void ToggleKeyVisible()
        {
            bool showing = _hostKey.Box.PasswordChar == '\0';
            _hostKey.Box.PasswordChar = showing ? '\u25CF' : '\0';
            _btnShow.Text = showing ? "显示密码" : "隐藏密码";
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
            _chipHidden.SetBounds(pad, y, Ui.Z(104), chipH);
            _chipAuto.SetBounds(pad + Ui.Z(112), y, Ui.Z(104), chipH);
            _chipConnect.SetBounds(pad + Ui.Z(224), y, Ui.Z(118), chipH);

            int btnH = Ui.Z(30);
            int btnY = this.ClientSize.Height - Ui.Z(20) - btnH;
            int rx = this.ClientSize.Width - pad;
            _btnSave.SetBounds(rx - Ui.Z(84), btnY, Ui.Z(84), btnH);
            _btnCancel.SetBounds(rx - Ui.Z(84) - Ui.Z(8) - Ui.Z(76), btnY, Ui.Z(76), btnH);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            int pad = Ui.Z(Pad);

            Ui.Text(g, "添加 WiFi", Ui.FTitle, Ui.Fg,
                new Rectangle(pad, Ui.Z(9), this.ClientSize.Width - pad * 2 - Ui.Z(40), Ui.FTitle.Height + Ui.Z(2)),
                Ui.LeftTop);

            using (Pen pen = new Pen(Ui.Line, 1F))
            {
                g.DrawLine(pen, pad, CapH - 1, this.ClientSize.Width - pad, CapH - 1);
            }

            TextRenderer.DrawText(g, "网络名称（SSID）", Ui.FKey, Ui.Mute, _lblSsid,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            TextRenderer.DrawText(g, "安全类型", Ui.FKey, Ui.Mute, _lblAuth,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            TextRenderer.DrawText(g, _lblKeyText, Ui.FKey, Ui.Mute, _lblKey,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }

        private void OnSave(object sender, EventArgs e)
        {
            string ssid = _hostSsid.Box.Text.Trim();
            if (ssid.Length == 0)
            {
                Prompt.Info(this, "无法保存", "请输入网络名称（SSID）。");
                _hostSsid.Box.Focus();
                return;
            }
            if (System.Text.Encoding.UTF8.GetByteCount(ssid) > 32)
            {
                Prompt.Info(this, "无法保存", "网络名称过长，最多 32 个字节（中文约 10 个字）。");
                _hostSsid.Box.Focus();
                return;
            }

            bool open = AuthCodes[_authIndex] == "open";
            string key = _hostKey.Box.Text;

            if (!open)
            {
                if (key.Length < 8 || key.Length > 63)
                {
                    Prompt.Info(this, "无法保存", "WPA 密码长度需为 8–63 个字符。\r\n\r\n当前长度：" + key.Length + " 个字符。");
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
}
