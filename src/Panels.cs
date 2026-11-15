using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WifiList
{
    /// <summary>Four KPI cards painted on one surface.</summary>
    internal sealed class StatsPanel : Panel
    {
        private WifiSnapshot _snap;

        public StatsPanel()
        {
            this.DoubleBuffered = true;
            this.ResizeRedraw = true;
            this.BackColor = Ui.Bg;
        }

        public void Bind(WifiSnapshot snap)
        {
            _snap = snap;
            this.Invalidate();
        }

        public void ApplyTheme()
        {
            this.BackColor = Ui.Bg;
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            int gap = Ui.Z(10);
            int w = (this.Width - gap * 3) / 4;
            if (w <= Ui.Z(20)) return;
            int h = this.Height;

            int total = 0;
            int withKey = 0;
            int open = 0;
            int auto = 0;
            string curSsid = "未连接";
            string curSub = "无线网卡未关联热点";

            if (_snap != null)
            {
                total = _snap.Nets.Count;
                for (int i = 0; i < _snap.Nets.Count; i++)
                {
                    WifiEntry n = _snap.Nets[i];
                    if (n.HasKey) withKey++;
                    if (n.Mode == "auto") auto++;
                }
                open = total - withKey;
                if (_snap.Current.Connected)
                {
                    curSsid = _snap.Current.Ssid.Length > 0 ? _snap.Current.Ssid : "已连接";
                    if (_snap.Current.HasRssi)
                    {
                        curSub = "信号 " + _snap.Current.Signal + "%  ·  " + _snap.Current.Rssi + " dBm";
                    }
                    else
                    {
                        curSub = "信号 " + _snap.Current.Signal + "%";
                    }
                }
            }

            string[] labels = new string[] { "已保存网络", "当前连接", "含密码", "开放网络" };
            string[] values = new string[] { total.ToString(), curSsid, withKey.ToString(), open.ToString() };
            string[] subs = new string[]
            {
                "自动连接 " + auto + " 个",
                curSub,
                "双击一行即可查看",
                open > 0 ? "无密码保护，连接即用" : "全部网络均有密码"
            };
            bool[] compact = new bool[] { false, true, false, false };

            int padIn = Ui.Z(15);
            int labelH = Ui.FCardLabel.Height + Ui.Z(2);
            int subH = Ui.FCardSub.Height + Ui.Z(2);

            for (int i = 0; i < 4; i++)
            {
                Rectangle card = new Rectangle(i * (w + gap), 0, w, h);
                Ui.FillRound(g, new Rectangle(card.X, card.Y, card.Width - 1, card.Height - 1),
                    Ui.Z(12), Ui.Panel, Ui.Line);

                int px = card.X + padIn;

                Ui.Text(g, labels[i], Ui.FCardLabel, Ui.Mute,
                    new Rectangle(px, card.Y + Ui.Z(10), card.Width - padIn * 2, labelH), Ui.LeftTop);

                Font vf = compact[i] ? Ui.FCardValueSm : Ui.FCardValue;
                int vy = compact[i] ? card.Y + Ui.Z(26) : card.Y + Ui.Z(25);
                Ui.Text(g, values[i], vf, Ui.Fg,
                    new Rectangle(px, vy, card.Width - padIn * 2, vf.Height + Ui.Z(4)), Ui.LeftTop);

                Ui.Text(g, subs[i], Ui.FCardSub, Ui.Mute,
                    new Rectangle(px, card.Y + card.Height - Ui.Z(9) - subH, card.Width - padIn * 2, subH),
                    Ui.LeftTop);
            }
        }
    }

    /// <summary>Current connection card with a signal meter and link details.</summary>
    internal sealed class CurrentPanel : Panel
    {
        private WifiSnapshot _snap;

        public CurrentPanel()
        {
            this.DoubleBuffered = true;
            this.ResizeRedraw = true;
            this.BackColor = Ui.Bg;
        }

        public void Bind(WifiSnapshot snap)
        {
            _snap = snap;
            this.Invalidate();
        }

        public void ApplyTheme()
        {
            this.BackColor = Ui.Bg;
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            Rectangle card = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            if (card.Width < Ui.Z(40) || card.Height < Ui.Z(30)) return;

            Ui.FillRound(g, card, Ui.Z(12), Ui.Panel, Ui.LineHi);

            NetworkInfo ci = (_snap == null) ? new NetworkInfo() : _snap.Current;

            int padL = Ui.Z(18);
            int kH = Ui.FKey.Height + Ui.Z(2);
            int vH = Ui.FVal.Height + Ui.Z(2);

            int y0 = card.Y + Ui.Z(11);

            // ---- signal ring (same idea as the HTML report) ----
            int pct = ci.Signal;
            if (pct < 0) pct = 0;
            if (pct > 100) pct = 100;

            int ringD = Ui.Z(76);
            int ringX = padL;
            int ringY = card.Y + (card.Height - ringD) / 2;
            int stroke = Math.Max(3, Ui.Z(6));
            Rectangle arc = new Rectangle(ringX + stroke / 2, ringY + stroke / 2,
                Math.Max(4, ringD - stroke), Math.Max(4, ringD - stroke));

            SmoothingMode oldMode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (Pen track = new Pen(Ui.LineHi, stroke))
            {
                track.StartCap = LineCap.Round;
                track.EndCap = LineCap.Round;
                g.DrawEllipse(track, arc);
            }

            float sweep = 3.6F * pct;
            if (sweep > 1F)
            {
                using (LinearGradientBrush grad = new LinearGradientBrush(arc, Ui.Accent, Ui.Accent2, 45F))
                using (Pen val = new Pen(grad, stroke))
                {
                    val.StartCap = LineCap.Round;
                    val.EndCap = LineCap.Round;
                    g.DrawArc(val, arc, -90F, sweep);
                }
            }

            g.SmoothingMode = oldMode;

            int textH = Ui.FRing.Height + Ui.FCardSub.Height + Ui.Z(1);
            int textY = ringY + (ringD - textH) / 2;
            Ui.Text(g, pct + "%", Ui.FRing, Ui.Fg,
                new Rectangle(ringX, textY, ringD, Ui.FRing.Height + Ui.Z(2)), Ui.CenterTop);
            Ui.Text(g, "信号", Ui.FCardSub, Ui.Mute,
                new Rectangle(ringX, textY + Ui.FRing.Height + Ui.Z(1), ringD,
                    Ui.FCardSub.Height + Ui.Z(2)), Ui.CenterTop);

            // ---- name block ----
            int midX = padL + Ui.Z(96);
            Ui.Text(g, "当前连接", Ui.FKey, Ui.Mute,
                new Rectangle(midX, y0, Ui.Z(280), kH), Ui.LeftTop);

            string ssid = ci.Connected ? (ci.Ssid.Length > 0 ? ci.Ssid : "已连接") : "未连接无线网络";
            int ssidY = y0 + kH;
            Ui.Text(g, ssid, Ui.FSsid, Ui.Fg,
                new Rectangle(midX, ssidY, Ui.Z(288), Ui.FSsid.Height + Ui.Z(2)), Ui.LeftTop);

            if (ci.Connected)
            {
                int lineY = ssidY + Ui.FSsid.Height + Ui.Z(1);
                Ui.Text(g, ci.Bssid.Length > 0 ? ci.Bssid : "BSSID 未知", Ui.FMono, Ui.Mute,
                    new Rectangle(midX, lineY, Ui.Z(288), Ui.FMono.Height + Ui.Z(2)), Ui.LeftTop);
                if (ci.Adapter.Length > 0)
                {
                    lineY += Ui.FMono.Height + Ui.Z(1);
                    Ui.Text(g, ci.Adapter, Ui.FMono, Ui.Mute,
                        new Rectangle(midX, lineY, Ui.Z(288), Ui.FMono.Height + Ui.Z(2)), Ui.LeftTop);
                }
            }

            // ---- details grid ----
            if (!ci.Connected)
            {
                int nx = midX + Ui.Z(296);
                int nw = this.Width - nx - padL;
                if (nw > Ui.Z(80))
                {
                    Ui.Text(g, "连接任意无线网络后，这里会显示 IP、网关、DNS 与链路速率。",
                        Ui.FEmpty, Ui.Mute,
                        new Rectangle(nx, y0 + Ui.Z(6), nw, Ui.FEmpty.Height + Ui.Z(2)), Ui.LeftTop);
                }
                return;
            }

            int gx = midX + Ui.Z(300);
            int gw = this.Width - gx - padL;
            if (gw < Ui.Z(170)) return;
            int colW = gw / 3;

            WifiEntry matched = null;
            if (_snap != null)
            {
                for (int i = 0; i < _snap.Nets.Count; i++)
                {
                    if (_snap.Nets[i].Current)
                    {
                        matched = _snap.Nets[i];
                        break;
                    }
                }
            }
            string secText = matched != null ? matched.SecurityText() : "-";
            string dns = ci.Dns.Count > 0 ? string.Join(", ", ci.Dns.ToArray()) : "-";
            string rssi = ci.HasRssi ? ci.Rssi + " dBm" : "-";

            string[] k1 = new string[] { "IP 地址", "网关", "DNS" };
            string[] v1 = new string[] { Safe(ci.Ip), Safe(ci.Gateway), dns };
            string[] k2 = new string[] { "链路速率", "安全类型", "RSSI" };
            string[] v2 = new string[] { Safe(ci.Speed), secText, rssi };

            int row2 = y0 + kH + vH + Ui.Z(12);
            for (int c = 0; c < 3; c++)
            {
                int cx = gx + c * colW;
                Ui.Text(g, k1[c], Ui.FKey, Ui.Mute, new Rectangle(cx, y0, colW - Ui.Z(8), kH), Ui.LeftTop);
                Ui.Text(g, v1[c], Ui.FVal, Ui.Fg, new Rectangle(cx, y0 + kH, colW - Ui.Z(8), vH), Ui.LeftTop);
                Ui.Text(g, k2[c], Ui.FKey, Ui.Mute, new Rectangle(cx, row2, colW - Ui.Z(8), kH), Ui.LeftTop);
                Ui.Text(g, v2[c], Ui.FVal, Ui.Fg, new Rectangle(cx, row2 + kH, colW - Ui.Z(8), vH), Ui.LeftTop);
            }
        }

        private static string Safe(string s)
        {
            return (s == null || s.Length == 0) ? "-" : s;
        }
    }
}
