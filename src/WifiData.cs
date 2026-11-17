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
using System.Threading.Tasks;
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

        /// <summary>
        /// Signal quality 0-100 as reported by the radio, or -1 when the
        /// network is not currently audible (an old profile, or the adapter is
        /// on another band). -1 must stay distinct from 0: 0 is a real reading
        /// at the edge of range.
        /// </summary>
        public int Signal = -1;

        public bool HasSignal
        {
            get { return this.Signal >= 0; }
        }

        public string AuthCode
        {
            get { return this.Auth == null ? "" : this.Auth.ToUpperInvariant(); }
        }

        public string AuthLabel()
        {
            string a = this.AuthCode;
            if (a == "OPEN") return L.T("a.open");
            if (a == "WPA3SAE") return "WPA3-SAE";
            if (a == "WPA3ENT192") return L.T("a.wpa3ent192");
            if (a == "WPA3ENT") return L.T("a.wpa3ent");
            if (a == "WPA2PSK") return "WPA2-PSK";
            if (a == "WPA2") return L.T("a.wpa2ent");
            if (a == "WPAPSK") return "WPA-PSK";
            if (a == "WPA") return L.T("a.wpaent");
            if (a == "WEP") return "WEP";
            if (a == "SHARED") return L.T("a.shared");
            return a.Length == 0 ? L.T("a.unknown") : a;
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
            return this.Mode == "auto" ? L.T("m.auto") : L.T("m.manual");
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

        /// <summary>True when this load asked the radio for a fresh scan. The
        /// results of that scan land a second or two later, so the window
        /// schedules a short settle pass instead of trusting the first read.</summary>
        public bool Scanned = false;
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
        /// <summary>When the radio was last asked to rescan (see Load).</summary>
        private static DateTime _lastScan = DateTime.MinValue;

        /// <summary>
        /// Text dump used by --diag-net. Shows every adapter, how it scores,
        /// which one wins, what the current-connection card would display and
        /// what the radio hears. Kept in the shipped build: when a machine
        /// reports odd IP/gateway values this answers "which adapter" in one
        /// command instead of guesswork.
        /// </summary>
        public static string Diagnose()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("== adapters (Up only) ==");

            try
            {
                NetworkInterface[] all = NetworkInterface.GetAllNetworkInterfaces();
                for (int i = 0; i < all.Length; i++)
                {
                    NetworkInterface ni = all[i];
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    bool up = ni.OperationalStatus == OperationalStatus.Up;
                    bool wifi = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
                    bool virt = IsVirtual(ni);

                    string ip = "", gw = "";
                    int dns = 0;
                    try
                    {
                        IPInterfaceProperties p = ni.GetIPProperties();
                        ip = FirstIpv4(p);
                        gw = FirstGateway(p);
                        dns = CountDns(p);
                    }
                    catch { }

                    int score = 0;
                    if (wifi) score += 100;
                    if (ip.Length > 0) score += 30;
                    if (gw.Length > 0) score += 40;
                    if (dns > 0) score += 10;

                    sb.AppendFormat(CultureInfo.InvariantCulture,
                        "  {0,-34} type={1} up={2} virtual={3} score={4} ip={5} gw={6} dns={7}",
                        ni.Name, ni.NetworkInterfaceType, up, virt, score,
                        ip.Length == 0 ? "-" : ip,
                        gw.Length == 0 ? "-" : gw, dns);
                    sb.AppendLine();
                    sb.AppendLine("      " + ni.Description);
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("  error: " + ex.Message);
            }

            try
            {
                NetworkInterface best = BestInterface();
                sb.AppendLine("winner: " + (best == null ? "(none — no physical adapter is Up)" :
                    best.Name + "  [" + best.Description + "]"));
            }
            catch (Exception ex)
            {
                sb.AppendLine("winner: error " + ex.Message);
            }

            NetworkInfo ci = GetCurrent();
            sb.AppendLine();
            sb.AppendLine("== current connection ==");
            sb.AppendLine("  connected=" + ci.Connected + "  ssid=" + (ci.Ssid.Length == 0 ? "-" : ci.Ssid) +
                "  bssid=" + (ci.Bssid.Length == 0 ? "-" : ci.Bssid));
            sb.AppendLine("  adapter=" + (ci.Adapter.Length == 0 ? "-" : ci.Adapter));
            sb.AppendLine("  ip=" + (ci.Ip.Length == 0 ? "-" : ci.Ip) +
                "  gateway=" + (ci.Gateway.Length == 0 ? "-" : ci.Gateway) +
                "  dns=" + (ci.Dns.Count == 0 ? "-" : string.Join(", ", ci.Dns.ToArray())));
            sb.AppendLine("  signal=" + ci.Signal + "%  rssi=" + (ci.HasRssi ? ci.Rssi.ToString(CultureInfo.InvariantCulture) : "-") +
                "  speed=" + (ci.Speed.Length == 0 ? "-" : ci.Speed));
            sb.AppendLine("  hasAddress=" + HasAddress());

            sb.AppendLine();
            sb.AppendLine("== audible networks ==");
            Dictionary<string, int> heard;
            if (!WlanApi.TryAvailableSignals(out heard) || heard.Count == 0)
            {
                sb.AppendLine("  (none readable through the WLAN API)");
            }
            else
            {
                List<string> names = new List<string>(heard.Keys);
                names.Sort(delegate(string a, string b) { return heard[b].CompareTo(heard[a]); });
                for (int i = 0; i < names.Count; i++)
                {
                    sb.AppendFormat(CultureInfo.InvariantCulture, "  {0,3}%  {1}", heard[names[i]], names[i]);
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        private static byte[] ReadAll(Stream s)
        {
            MemoryStream ms = new MemoryStream();
            byte[] buf = new byte[8192];
            int n;
            while ((n = s.Read(buf, 0, buf.Length)) > 0)
            {
                ms.Write(buf, 0, n);
            }
            return ms.ToArray();
        }

        /// <summary>
        /// Decodes a netsh byte stream. Current Windows builds send redirected
        /// netsh output as UTF-8 while older components still use the ANSI code
        /// page; decoding with the wrong one produced unreadable error dialogs
        /// ("闂晱鑼囧洖..." style). A strict UTF-8 attempt disambiguates the two,
        /// because GBK text is almost never valid UTF-8.
        /// </summary>
        private static string Decode(byte[] data)
        {
            if (data == null || data.Length == 0) return "";

            if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
            {
                return Encoding.Unicode.GetString(data, 2, data.Length - 2);
            }
            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            {
                return Encoding.UTF8.GetString(data, 3, data.Length - 3);
            }

            try
            {
                return new UTF8Encoding(false, true).GetString(data);
            }
            catch (Exception)
            {
                return Encoding.Default.GetString(data);
            }
        }

        /// <summary>
        /// Runs netsh and decodes both streams. Output is captured as raw bytes
        /// and decoded afterwards, and both pipes are drained in parallel so a
        /// long error message can never deadlock the read.
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
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;

                using (Process p = Process.Start(psi))
                {
                    Task<byte[]> outTask = Task.Factory.StartNew<byte[]>(
                        delegate { return ReadAll(p.StandardOutput.BaseStream); });
                    Task<byte[]> errTask = Task.Factory.StartNew<byte[]>(
                        delegate { return ReadAll(p.StandardError.BaseStream); });

                    if (!p.WaitForExit(30000))
                    {
                        try { p.Kill(); } catch { }
                    }

                    string text = "";
                    string err = "";
                    if (outTask.Wait(3000)) { try { text = Decode(outTask.Result); } catch { } }
                    if (errTask.Wait(3000)) { try { err = Decode(errTask.Result); } catch { } }

                    result.Text = text;
                    if (err.Length > 0) result.Text = result.Text + "\n" + err;
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

            // Nudge the radio so the signal column reflects the neighbourhood
            // now rather than whenever Windows last scanned. The call returns
            // immediately; results land in the next read. Throttled because a
            // burst of refreshes must not keep the radio hopping channels.
            try
            {
                if ((DateTime.UtcNow - _lastScan).TotalSeconds >= 5)
                {
                    _lastScan = DateTime.UtcNow;
                    WlanApi.Scan();
                    snap.Scanned = true;
                }
            }
            catch { }

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
                    snap.Error = L.T("w.errNoProfiles");
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

            // Signal strength for every saved profile the radio can hear.
            // Profiles out of range keep -1 and sink to the bottom of the list.
            ApplySignals(snap, true);

            SortBySignal(snap.Nets);

            return snap;
        }

        /// <summary>Reads the neighbourhood and writes the values into
        /// <paramref name="snap"/>. Returns true when at least one profile's
        /// value actually moved.
        ///
        /// <paramref name="allowClear"/> decides what happens to a profile the
        /// radio does not report: a full load starts from a blank snapshot and
        /// must clear it, while a settle pass runs *while* the scan is still in
        /// flight and gets a partial list — clearing there would make rows
        /// flicker between "91%" and "—".</summary>
        public static bool ApplySignals(WifiSnapshot snap, bool allowClear)
        {
            bool changed = false;
            try
            {
                // The connected profile is read from a different source than the
                // rest of the list: "netsh wlan show interfaces" reports the
                // association itself, while the list comes from the last scan
                // snapshot; the first entry of a given name may even be
                // another access point of the same SSID. That showed up as 97%
                // in the signal ring and 99% on the row of the same network, so
                // the association value wins for that row.
                int assoc = snap.Current.Connected ? snap.Current.Signal : -1;

                Dictionary<string, int> heard;
                bool haveList = WlanApi.TryAvailableSignals(out heard) && heard.Count > 0;
                if (!haveList && assoc <= 0)
                {
                    return false;
                }

                for (int i = 0; i < snap.Nets.Count; i++)
                {
                    WifiEntry n = snap.Nets[i];
                    bool isCurrent = assoc > 0 &&
                        (n.Current || string.Equals(n.Ssid, snap.Current.Ssid, StringComparison.Ordinal));

                    int q = -1;
                    bool heardThis = haveList && heard.TryGetValue(n.Ssid, out q);

                    if (isCurrent)
                    {
                        q = assoc;
                    }
                    else if (!heardThis)
                    {
                        if (allowClear && n.Signal >= 0)
                        {
                            n.Signal = -1;
                            changed = true;
                        }
                        continue;
                    }

                    if (n.Signal != q)
                    {
                        n.Signal = q;
                        changed = true;
                    }
                }
            }
            catch { }
            return changed;
        }

        /// <summary>Re-reads the radio after a scan was requested and applies
        /// only what has become audible. Used by the window's settle timer:
        /// WlanScan returns immediately, so the read that follows it a few
        /// hundred milliseconds later can still be missing the second access
        /// point — which is why a network used to appear only on the second
        /// refresh.</summary>
        public static bool RefreshSignals(WifiSnapshot snap)
        {
            if (snap == null) return false;
            bool changed = ApplySignals(snap, false);
            if (changed) SortBySignal(snap.Nets);
            return changed;
        }

        /// <summary>Audible networks first, strongest to weakest; the rest by
        /// name. Signal 0 is a real reading at the edge of range and must rank
        /// above "not heard at all" (-1).</summary>
        public static void SortBySignal(List<WifiEntry> nets)
        {
            nets.Sort(delegate(WifiEntry a, WifiEntry b)
            {
                bool ha = a.HasSignal;
                bool hb = b.HasSignal;
                if (ha != hb) return ha ? -1 : 1;
                if (ha && a.Signal != b.Signal) return a.Signal > b.Signal ? -1 : 1;
                return string.Compare(a.Ssid, b.Ssid, StringComparison.CurrentCulture);
            });
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

        /// <summary>
        /// Picks the interface that actually carries the connection: a physical
        /// wireless adapter with an address, a gateway and DNS wins.
        ///
        /// Virtual adapters are skipped outright rather than merely ranked
        /// lower. The Wi-Fi Direct adapter is itself a Wireless80211 interface
        /// with an address (192.168.137.1, the ICS range) but no gateway and no
        /// DNS, so a penalty alone decided nothing whenever it was the only
        /// candidate left. That is exactly what happened right after switching
        /// networks: the real card is briefly Down while the old association
        /// tears down, it got skipped by the Up check, and the virtual adapter
        /// won by default — the connection card then showed its address and
        /// dashes for gateway/DNS/link speed until the app was restarted.
        ///
        /// Returning null is better than returning a virtual adapter: the
        /// caller shows the saved previous values / dashes instead of a
        /// plausible but wrong address, and the delayed refresh picks the real
        /// card up as soon as it is back.
        /// </summary>
        private static NetworkInterface BestInterface()
        {
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

                    // Never let a virtual adapter stand in for the connection.
                    if (IsVirtual(ni)) continue;

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

            return best;
        }

        /// <summary>
        /// The name netsh knows the wireless interface by (its connection name,
        /// e.g. "WLAN"). Passing it explicitly to "wlan connect" avoids netsh
        /// guessing between the physical adapter and the Wi-Fi Direct one.
        /// </summary>
        public static string WirelessInterfaceName()
        {
            NetworkInterface ni = BestInterface();
            if (ni == null) return "";
            // Only a real wireless adapter is a valid netsh target; when the
            // winner is Ethernet the caller falls back to netsh's default.
            if (ni.NetworkInterfaceType != NetworkInterfaceType.Wireless80211) return "";
            return ni.Name == null ? "" : ni.Name;
        }

        /// <summary>
        /// True once a physical adapter holds an IPv4 address. Cheap check used
        /// to wait out DHCP after a switch, without running netsh in a loop.
        /// </summary>
        public static bool HasAddress()
        {
            NetworkInterface ni = BestInterface();
            if (ni == null) return false;
            try { return FirstIpv4(ni.GetIPProperties()).Length > 0; }
            catch { return false; }
        }

        public static NetworkInfo GetCurrent()
        {
            NetworkInfo info = new NetworkInfo();

            // The WLAN layer is read first on purpose. netsh still reports the
            // SSID, BSSID and signal while the adapter is being reconfigured,
            // whereas the network stack (address, gateway, DNS, link speed) is
            // empty for a moment after every switch. Reading the stack first
            // used to leave the whole card without even a network name.
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

            // Being associated is enough to call it connected, even if the
            // address has not arrived yet.
            if (info.Ssid.Length > 0) info.Connected = true;

            // Only a physical adapter carries address, gateway, DNS and link
            // speed; BestInterface returns null while it is between states.
            NetworkInterface best = BestInterface();
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

            return info;
        }

        // ------------------------------------------------------------------
        // profile management
        // ------------------------------------------------------------------

        /// <summary>
        /// Deletes a saved profile. netsh exits 0 even when it did nothing, so
        /// its answer alone is never trusted: the profile list is re-read and,
        /// if the profile is still there, it is removed through the native API.
        ///
        /// This matters because a leading '#' makes netsh read the rest of the
        /// argument as a comment: "wlan delete profile name=#foo" looks up a
        /// profile called "" and reports success without deleting anything.
        /// </summary>
        public static NetshResult DeleteProfile(string ssid)
        {
            NetshResult r = Run("wlan delete profile name=\"" + ssid + "\"");

            List<string> names;
            if (!WlanApi.TryProfileNames(out names)) return r;   // no API: keep old behaviour
            if (!HasName(names, ssid)) return r;                 // netsh did delete it

            NetshResult api = WlanApi.Delete(ssid);
            if (!api.Ok)
            {
                api.Text = r.Text + "\n" + api.Text;
                return api;
            }

            List<string> after;
            if (WlanApi.TryProfileNames(out after) && HasName(after, ssid))
            {
                api.Code = 3;
                api.Text = L.F("w.apiStillThere", ssid);
            }
            return api;
        }

        /// <summary>
        /// True when netsh cannot be trusted with this name as an argument.
        /// '#' at the start begins a comment and a quote breaks the argument
        /// parsing; neither can be escaped from the outside.
        /// </summary>
        public static bool NameNeedsApi(string ssid)
        {
            if (ssid == null || ssid.Length == 0) return false;
            if (ssid[0] == '#') return true;
            return ssid.IndexOf('"') >= 0;
        }

        private static bool HasName(List<string> names, string ssid)
        {
            for (int i = 0; i < names.Count; i++)
            {
                if (string.Equals(names[i], ssid, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        public static NetshResult Connect(string ssid)
        {
            return Connect(ssid, "");
        }

        /// <summary>
        /// Asks the WLAN service to associate with a saved profile. The
        /// interface is named explicitly when known: "wlan connect" otherwise
        /// lets netsh choose, and on machines that also expose the Wi-Fi Direct
        /// adapter it can answer "无法连接…" while the Windows flyout still works.
        /// </summary>
        public static NetshResult Connect(string ssid, string iface)
        {
            if (NameNeedsApi(ssid)) return WlanApi.Connect(ssid);

            // ssid= helps for hidden networks; the interface pins the adapter.
            string args = "wlan connect name=\"" + ssid + "\" ssid=\"" + ssid + "\"";
            if (iface != null && iface.Length > 0)
            {
                args = args + " interface=\"" + iface + "\"";
            }
            return Run(args);
        }

        public static NetshResult Disconnect()
        {
            return Run("wlan disconnect");
        }

        /// <summary>Switches a saved profile between automatic and manual connection.</summary>
        public static NetshResult SetConnectionMode(string ssid, bool auto)
        {
            if (!NameNeedsApi(ssid))
            {
                return Run("wlan set profileparameter name=\"" + ssid + "\" connectionmode=" +
                    (auto ? "auto" : "manual"));
            }
            return RewriteConnectionMode(ssid, auto);
        }

        /// <summary>
        /// The same switch without a name argument: export the profile, flip
        /// &lt;connectionMode&gt; in the document and write it back. Re-importing
        /// Windows' own copy keeps everything else — key material, 802.1X,
        /// MAC randomisation — exactly as it was.
        /// </summary>
        private static NetshResult RewriteConnectionMode(string ssid, bool auto)
        {
            string dir = NewTempDir("wifi_mode_");
            try
            {
                Run("wlan export profile folder=\"" + dir + "\" key=clear");
                string file = FindProfileXml(dir, ssid);
                if (file == null)
                {
                    NetshResult miss = new NetshResult();
                    miss.Code = 2;
                    miss.Text = L.F("w.exportFail", ssid);
                    return miss;
                }

                string body = File.ReadAllText(file);
                string want = "<connectionMode>" + (auto ? "auto" : "manual") + "</connectionMode>";
                string replaced = Regex.Replace(body,
                    @"<connectionMode>\s*(auto|manual)\s*</connectionMode>", want,
                    RegexOptions.IgnoreCase);
                if (body.IndexOf("<connectionMode", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    replaced = body.Replace("</connectionType>",
                        "</connectionType>\r\n  " + want);
                }

                File.WriteAllText(file, replaced, new UTF8Encoding(true));
                return Run("wlan add profile filename=\"" + file + "\" user=current");
            }
            catch (Exception ex)
            {
                NetshResult bad = new NetshResult();
                bad.Code = -1;
                bad.Text = ex.Message;
                return bad;
            }
            finally
            {
                DiscardDir(dir);
            }
        }

        /// <summary>
        /// Exports a profile to a temp folder before it is overwritten, so a
        /// failed edit can be rolled back. Returns the xml path, or "" if the
        /// export failed (the caller then keeps the current profile untouched).
        /// </summary>
        public static string BackupProfile(string ssid)
        {
            string dir = NewTempDir("wifi_bak_");
            try
            {
                Run("wlan export profile name=\"" + ssid + "\" folder=\"" + dir + "\" key=clear");
                string file = FindProfileXml(dir, ssid);
                if (file == null)
                {
                    // netsh drops a name that starts with '#', so the by-name
                    // export produces nothing. Dumping every profile and picking
                    // ours by content is the only way netsh can still hand it over.
                    Run("wlan export profile folder=\"" + dir + "\" key=clear");
                    file = FindProfileXml(dir, ssid);
                }
                if (file != null) return file;

                DiscardDir(dir);
                return "";
            }
            catch
            {
                DiscardDir(dir);
                return "";
            }
        }

        private static string NewTempDir(string prefix)
        {
            string dir = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
            try { Directory.CreateDirectory(dir); }
            catch { }
            return dir;
        }

        private static void DiscardDir(string dir)
        {
            try
            {
                if (dir != null && dir.Length > 0 && Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
            catch { }
        }

        /// <summary>Finds the exported xml of one profile inside a folder.</summary>
        private static string FindProfileXml(string dir, string ssid)
        {
            try
            {
                if (!Directory.Exists(dir)) return null;
                string[] files = Directory.GetFiles(dir, "*.xml");
                for (int i = 0; i < files.Length; i++)
                {
                    try
                    {
                        XmlDocument doc = new XmlDocument();
                        doc.Load(files[i]);
                        if (string.Equals(NodeText(doc, "name"), ssid, StringComparison.Ordinal))
                        {
                            return files[i];
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }

        /// <summary>Re-imports a profile that <see cref="BackupProfile"/> exported.</summary>
        public static NetshResult RestoreProfile(string xmlPath)
        {
            if (xmlPath == null || xmlPath.Length == 0 || !File.Exists(xmlPath))
            {
                NetshResult bad = new NetshResult();
                bad.Text = L.T("w.errNoBackup");
                bad.Code = -1;
                return bad;
            }
            return Run("wlan add profile filename=\"" + xmlPath + "\" user=current");
        }

        public static void DropBackup(string xmlPath)
        {
            if (xmlPath == null || xmlPath.Length == 0) return;
            try
            {
                string dir = Path.GetDirectoryName(xmlPath);
                if (File.Exists(xmlPath)) File.Delete(xmlPath);
                if (dir != null && Directory.Exists(dir) && Directory.GetFiles(dir).Length == 0)
                {
                    Directory.Delete(dir, true);
                }
            }
            catch { }
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
