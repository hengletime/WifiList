using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace WifiList
{
    /// <summary>One saved wireless profile.</summary>
    internal sealed class WifiEntry
    {
        public string Ssid = "";
        public string Auth = "";
        public string Enc = "";
        public string Key = "";
        public bool HasKey = false;
        public string Mode = "";
        public bool OneX = false;
        public bool Hidden = false;
        public bool Current = false;

        public string AuthCode
        {
            get { return this.Auth == null ? "" : this.Auth.ToUpperInvariant(); }
        }

        public string AuthLabel()
        {
            string a = this.AuthCode;
            if (a == "OPEN") return "开放网络";
            if (a == "WPA3SAE") return "WPA3-SAE";
            if (a == "WPA3ENT192") return "WPA3 企业 192";
            if (a == "WPA3ENT") return "WPA3 企业";
            if (a == "WPA2PSK") return "WPA2-PSK";
            if (a == "WPA2") return "WPA2 企业";
            if (a == "WPAPSK") return "WPA-PSK";
            if (a == "WPA") return "WPA 企业";
            if (a == "WEP") return "WEP";
            if (a == "SHARED") return "WEP 共享";
            return a.Length == 0 ? "未知" : a;
        }

        public string SecurityText()
        {
            string label = AuthLabel();
            bool open = (this.AuthCode == "OPEN");
            if (!open && this.Enc != null && this.Enc.Length > 0 && this.Enc != "none")
            {
                return label + " · " + this.Enc;
            }
            return label;
        }

        public string ModeLabel()
        {
            return this.Mode == "auto" ? "自动连接" : "手动连接";
        }

        /// <summary>Badge colour tier derived from the authentication method.</summary>
        public string Tier()
        {
            string a = this.AuthCode;
            if (a == "OPEN") return "open";
            if (a.StartsWith("WPA3", StringComparison.Ordinal)) return "wpa3";
            if (a.StartsWith("WPA2", StringComparison.Ordinal)) return "wpa2";
            if (a.StartsWith("WEP", StringComparison.Ordinal) || a == "SHARED") return "wep";
            return "wpa";
        }
    }

    /// <summary>State of the currently associated network.</summary>
    internal sealed class NetworkInfo
    {
        public bool Connected = false;
        public string Ssid = "";
        public string Bssid = "";
        public int Signal = 0;
        public int Rssi = 0;
        public bool HasRssi = false;
        public string Speed = "";
        public string Ip = "";
        public string Gateway = "";
        public List<string> Dns = new List<string>();
        public string Adapter = "";
    }

    internal sealed class WifiSnapshot
    {
        public List<WifiEntry> Nets = new List<WifiEntry>();
        public NetworkInfo Current = new NetworkInfo();
        public string Adapter = "";
        public string Error = "";
    }

    /// <summary>Outcome of a netsh invocation.</summary>
    internal sealed class NetshResult
    {
        public string Text = "";
        public int Code = 0;

        public bool Ok
        {
            get { return Code == 0; }
        }
    }

    internal static class WifiCollector
    {
        /// <summary>
        /// Runs netsh and decodes its output with the system ANSI code page.
        /// netsh writes localised text in the OEM/ANSI code page, so this is the
        /// only way to keep non-ASCII SSIDs intact.
        /// </summary>
        public static NetshResult Run(string arguments)
        {
            NetshResult result = new NetshResult();
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("netsh", arguments);
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = Encoding.Default;
                psi.StandardErrorEncoding = Encoding.Default;
                using (Process p = Process.Start(psi))
                {
                    string text = p.StandardOutput.ReadToEnd();
                    string err = p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(30000))
                    {
                        try { p.Kill(); } catch { }
                    }
                    result.Text = text;
                    if (!string.IsNullOrEmpty(err)) result.Text = result.Text + "\n" + err;
                    try { result.Code = p.ExitCode; } catch { result.Code = 0; }
                }
            }
            catch (Exception ex)
            {
                result.Text = ex.Message;
                result.Code = -1;
            }
            return result;
        }

        public static string RunNetsh(string arguments)
        {
            return Run(arguments).Text;
        }

        private static string NodeText(XmlDocument doc, string localName)
        {
            XmlNode n = doc.SelectSingleNode("//*[local-name()='" + localName + "']");
            return n == null ? "" : n.InnerText;
        }

        public static WifiSnapshot Load()
        {
            WifiSnapshot snap = new WifiSnapshot();
            snap.Current = GetCurrent();

            string tmp = Path.Combine(Path.GetTempPath(), "wifi_export_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tmp);

                // Export every profile with its plain text key. This produces a
                // standard WLANProfile XML per network, which is language neutral
                // (unlike parsing the localised "show profiles" text output).
                Run("wlan export profile folder=\"" + tmp + "\" key=clear");

                string[] files = Directory.GetFiles(tmp, "*.xml");
                for (int i = 0; i < files.Length; i++)
                {
                    try
                    {
                        XmlDocument doc = new XmlDocument();
                        doc.Load(files[i]);

                        WifiEntry e = new WifiEntry();
                        e.Ssid = NodeText(doc, "name");
                        if (e.Ssid.Length == 0)
                        {
                            e.Ssid = Path.GetFileNameWithoutExtension(files[i]);
                        }
                        e.Auth = NodeText(doc, "authentication");
                        e.Enc = NodeText(doc, "encryption");
                        e.Mode = NodeText(doc, "connectionMode");
                        e.OneX = NodeText(doc, "useOneX") == "true";
                        e.Hidden = NodeText(doc, "nonBroadcast") == "true";
                        e.HasKey = NodeText(doc, "keyType").Length > 0;
                        e.Key = NodeText(doc, "keyMaterial");
                        if (NodeText(doc, "protected") == "true")
                        {
                            e.Key = "";
                        }
                        snap.Nets.Add(e);
                    }
                    catch
                    {
                        // a single unreadable profile must not abort the whole run
                    }
                }

                if (snap.Nets.Count == 0)
                {
                    snap.Error = "没有导出任何 WiFi 配置。请确认无线网卡已启用，且至少连接过一个网络。";
                }
            }
            catch (Exception ex)
            {
                snap.Error = ex.Message;
            }
            finally
            {
                try { Directory.Delete(tmp, true); } catch { }
            }

            // mark the currently associated profile
            if (snap.Current.Connected && snap.Current.Ssid.Length > 0)
            {
                for (int i = 0; i < snap.Nets.Count; i++)
                {
                    if (string.Equals(snap.Nets[i].Ssid, snap.Current.Ssid, StringComparison.Ordinal))
                    {
                        snap.Nets[i].Current = true;
                        break;
                    }
                }
            }

            // sort: current first, then by name
            snap.Nets.Sort(delegate(WifiEntry a, WifiEntry b)
            {
                if (a.Current != b.Current) return a.Current ? -1 : 1;
                return string.Compare(a.Ssid, b.Ssid, StringComparison.CurrentCulture);
            });

            return snap;
        }

        // ------------------------------------------------------------------
        // adapter selection
        // ------------------------------------------------------------------

        /// <summary>
        /// Virtual adapters (Wi-Fi Direct, Hyper-V, VPN taps) also report
        /// Wireless80211 and can be Up, but they carry no default gateway and
        /// no DNS servers. They must never win, otherwise the current
        /// connection card shows dashes for gateway/DNS/link speed.
        /// </summary>
        private static bool IsVirtual(NetworkInterface ni)
        {
            string d = (ni.Description == null ? "" : ni.Description).ToLowerInvariant();
            string n = (ni.Name == null ? "" : ni.Name).ToLowerInvariant();
            string[] bad = new string[]
            {
                "virtual", "wi-fi direct", "wifi direct", "miniport", "hyper-v",
                "vmware", "virtualbox", "loopback", "pseudo", "tap-", "tunnel",
                "wan miniport", "bluetooth"
            };
            for (int i = 0; i < bad.Length; i++)
            {
                if (d.IndexOf(bad[i], StringComparison.Ordinal) >= 0) return true;
                if (n.IndexOf(bad[i], StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        private static string FirstIpv4(IPInterfaceProperties props)
        {
            if (props == null) return "";
            foreach (UnicastIPAddressInformation ua in props.UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                string s = ua.Address.ToString();
                if (s.StartsWith("169.254.", StringComparison.Ordinal)) continue;
                return s;
            }
            return "";
        }

        private static string FirstGateway(IPInterfaceProperties props)
        {
            if (props == null) return "";
            foreach (GatewayIPAddressInformation g in props.GatewayAddresses)
            {
                if (g.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                string s = g.Address.ToString();
                if (s == "0.0.0.0") continue;
                return s;
            }
            return "";
        }

        private static int CountDns(IPInterfaceProperties props)
        {
            if (props == null) return 0;
            int n = 0;
            foreach (IPAddress d in props.DnsAddresses)
            {
                if (d.AddressFamily == AddressFamily.InterNetwork) n++;
            }
            return n;
        }

        public static NetworkInfo GetCurrent()
        {
            NetworkInfo info = new NetworkInfo();

            NetworkInterface best = null;
            int bestScore = int.MinValue;

            try
            {
                NetworkInterface[] all = NetworkInterface.GetAllNetworkInterfaces();
                for (int i = 0; i < all.Length; i++)
                {
                    NetworkInterface ni = all[i];
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    bool wifi = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;

                    IPInterfaceProperties props = null;
                    try { props = ni.GetIPProperties(); } catch { }

                    string ip = FirstIpv4(props);
                    string gw = FirstGateway(props);
                    int dns = CountDns(props);

                    // A routed interface is required; skip anything that has
                    // neither an address nor a gateway.
                    if (ip.Length == 0 && gw.Length == 0 && !wifi) continue;

                    int score = 0;
                    if (wifi) score += 100;
                    if (IsVirtual(ni)) score -= 250;
                    if (ip.Length > 0) score += 30;
                    if (gw.Length > 0) score += 40;
                    if (dns > 0) score += 10;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = ni;
                    }
                }
            }
            catch { }

            if (best == null)
            {
                return info;
            }

            info.Connected = true;
            info.Adapter = (best.Description != null && best.Description.Length > 0) ? best.Description : best.Name;

            try
            {
                IPInterfaceProperties props = best.GetIPProperties();
                info.Ip = FirstIpv4(props);
                info.Gateway = FirstGateway(props);
                foreach (IPAddress dns in props.DnsAddresses)
                {
                    if (dns.AddressFamily == AddressFamily.InterNetwork)
                    {
                        info.Dns.Add(dns.ToString());
                    }
                }
            }
            catch { }

            try
            {
                long bps = best.Speed;
                if (bps > 0)
                {
                    info.Speed = (bps / 1000000.0).ToString("0.#", CultureInfo.InvariantCulture) + " Mbps";
                }
            }
            catch { }

            try
            {
                string raw = RunNetsh("wlan show interfaces");

                // More than one block is printed when several wireless
                // interfaces exist (the Wi-Fi Direct virtual adapter has its
                // own). Only the associated one carries an "AP BSSID" line.
                string block = raw;
                string[] blocks = raw.Split(new string[] { "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < blocks.Length; i++)
                {
                    if (blocks[i].IndexOf("BSSID", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        block = blocks[i];
                        break;
                    }
                }

                // "AP BSSID" and "Rssi" are ASCII labels in every locale
                Match mBssid = Regex.Match(block, @"BSSID\s*:\s*([0-9a-fA-F]{2}(?::[0-9a-fA-F]{2}){5})");
                if (mBssid.Success)
                {
                    info.Bssid = mBssid.Groups[1].Value.ToLowerInvariant();
                }

                Match mRssi = Regex.Match(block, @"Rssi\s*:\s*(-?\d+)");
                if (mRssi.Success)
                {
                    int parsed;
                    if (int.TryParse(mRssi.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                    {
                        info.Rssi = parsed;
                        info.HasRssi = true;
                    }
                }

                Match mPct = Regex.Match(block, @"(\d{1,3})\s*%");
                if (mPct.Success)
                {
                    int pct;
                    if (int.TryParse(mPct.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out pct))
                    {
                        info.Signal = pct;
                    }
                }
                if (info.Signal <= 0 && info.HasRssi)
                {
                    info.Signal = Math.Max(0, Math.Min(100, 2 * (info.Rssi + 100)));
                }

                Match mSsid = Regex.Match(block, @"^[ \t]*SSID[ \t]*:[ \t]*(.+?)[ \t]*$", RegexOptions.Multiline);
                if (mSsid.Success)
                {
                    info.Ssid = mSsid.Groups[1].Value.Trim();
                }
            }
            catch { }

            return info;
        }

        // ------------------------------------------------------------------
        // profile management
        // ------------------------------------------------------------------

        public static NetshResult DeleteProfile(string ssid)
        {
            return Run("wlan delete profile name=\"" + ssid + "\"");
        }

        public static NetshResult Connect(string ssid)
        {
            return Run("wlan connect name=\"" + ssid + "\"");
        }

        private static string XmlEscape(string s)
        {
            if (s == null) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        /// <summary>Builds a WLANProfile document compatible with netsh wlan add profile.</summary>
        private static string BuildProfileXml(string ssid, string auth, string enc, string key, bool hidden, bool auto)
        {
            StringBuilder hex = new StringBuilder();
            byte[] raw = Encoding.UTF8.GetBytes(ssid);
            for (int i = 0; i < raw.Length; i++)
            {
                hex.Append(raw[i].ToString("X2", CultureInfo.InvariantCulture));
            }

            StringBuilder sb = new StringBuilder(1024);
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n");
            sb.Append("<WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\">\r\n");
            sb.Append("  <name>").Append(XmlEscape(ssid)).Append("</name>\r\n");
            sb.Append("  <SSIDConfig>\r\n");
            sb.Append("    <SSID>\r\n");
            sb.Append("      <hex>").Append(hex.ToString()).Append("</hex>\r\n");
            sb.Append("      <name>").Append(XmlEscape(ssid)).Append("</name>\r\n");
            sb.Append("    </SSID>\r\n");
            if (hidden) sb.Append("    <nonBroadcast>true</nonBroadcast>\r\n");
            sb.Append("  </SSIDConfig>\r\n");
            sb.Append("  <connectionType>ESS</connectionType>\r\n");
            sb.Append("  <connectionMode>").Append(auto ? "auto" : "manual").Append("</connectionMode>\r\n");
            sb.Append("  <MSM>\r\n");
            sb.Append("    <security>\r\n");
            sb.Append("      <authEncryption>\r\n");
            sb.Append("        <authentication>").Append(auth).Append("</authentication>\r\n");
            sb.Append("        <encryption>").Append(enc).Append("</encryption>\r\n");
            sb.Append("        <useOneX>false</useOneX>\r\n");
            sb.Append("      </authEncryption>\r\n");
            if (!string.Equals(auth, "open", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append("      <sharedKey>\r\n");
                sb.Append("        <keyType>passPhrase</keyType>\r\n");
                sb.Append("        <protected>false</protected>\r\n");
                sb.Append("        <keyMaterial>").Append(XmlEscape(key)).Append("</keyMaterial>\r\n");
                sb.Append("      </sharedKey>\r\n");
            }
            sb.Append("    </security>\r\n");
            sb.Append("  </MSM>\r\n");
            sb.Append("</WLANProfile>\r\n");
            return sb.ToString();
        }

        public static NetshResult AddProfile(string ssid, string auth, string enc, string key, bool hidden, bool auto)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "wifi_add_" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                // UTF-8 with BOM keeps non-ASCII SSIDs intact for netsh.
                File.WriteAllText(tmp, BuildProfileXml(ssid, auth, enc, key, hidden, auto), new UTF8Encoding(true));
                return Run("wlan add profile filename=\"" + tmp + "\" user=current");
            }
            catch (Exception ex)
            {
                NetshResult r = new NetshResult();
                r.Text = ex.Message;
                r.Code = -1;
                return r;
            }
            finally
            {
                try { File.Delete(tmp); } catch { }
            }
        }
    }
}
