using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.Win32;

namespace WifiList
{
    /// <summary>
    /// Renders the collected data into the self contained HTML report.
    /// The HTML template is embedded in the executable, so the shipped
    /// program is a single self sufficient file.
    /// </summary>
    internal static class HtmlExporter
    {
        public const string TemplateResource = "WifiList.Template.html";

        public static string DefaultPath()
        {
            string dir = Path.Combine(ApplicationRoot(), "output");
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }
            catch { }
            return Path.Combine(dir, "wifi-list-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".html");
        }

        public static string ApplicationRoot()
        {
            try
            {
                string exe = Assembly.GetExecutingAssembly().Location;
                string dir = Path.GetDirectoryName(exe);
                if (!string.IsNullOrEmpty(dir)) return dir;
            }
            catch { }
            return Directory.GetCurrentDirectory();
        }

        public static string Export(string outPath, WifiSnapshot snap)
        {
            if (snap == null) throw new ArgumentNullException("snap");
            string template = ReadTemplate();
            string json = BuildJson(snap);
            string html = template.Replace("/*__WIFI_DATA__*/", json);
            html = html.Replace("/*__WIFI_LANG__*/", "\"" + LangTag() + "\"");

            string dir = Path.GetDirectoryName(outPath);
            if (dir != null && dir.Length > 0 && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(outPath, html, new UTF8Encoding(false));
            return outPath;
        }

        /// <summary>Language tag handed to the report so it opens in the same language.</summary>
        private static string LangTag()
        {
            if (L.Current == UiLang.En) return "en";
            if (L.Current == UiLang.Th) return "th";
            return "zh";
        }

        private static string ReadTemplate()
        {
            Assembly asm = typeof(HtmlExporter).Assembly;
            Stream s = asm.GetManifestResourceStream(TemplateResource);
            if (s == null)
            {
                throw new InvalidOperationException(L.T("html.noTemplate") + " " + TemplateResource);
            }
            using (s)
            {
                using (StreamReader reader = new StreamReader(s, Encoding.UTF8, true))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        /// <summary>JSON string literal, hardened against breaking out of &lt;script&gt;.</summary>
        private static string J(string value)
        {
            string s = value == null ? "" : value;
            StringBuilder sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '<': sb.Append("\\u003c"); break;
                    case '>': sb.Append("\\u003e"); break;
                    case '&': sb.Append("\\u0026"); break;
                    default:
                        if (c < ' ')
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            return sb.ToString();
        }

        private static string Bool(bool b)
        {
            return b ? "true" : "false";
        }

        private static string OsName()
        {
            try
            {
                object v = Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion",
                    "ProductName", null);
                string name = v as string;
                if (!string.IsNullOrEmpty(name))
                {
                    return name;
                }
            }
            catch { }
            return Environment.OSVersion.VersionString;
        }

        private static string BuildJson(WifiSnapshot snap)
        {
            StringBuilder sb = new StringBuilder(4096);
            NetworkInfo ci = snap.Current;

            sb.Append("{\"meta\":{");
            sb.Append("\"host\":\"").Append(J(Environment.MachineName)).Append("\",");
            sb.Append("\"os\":\"").Append(J(OsName())).Append("\",");
            sb.Append("\"time\":\"").Append(J(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))).Append("\",");
            sb.Append("\"adapter\":\"").Append(J(string.IsNullOrEmpty(ci.Adapter) ? snap.Adapter : ci.Adapter)).Append("\",");
            sb.Append("\"total\":").Append(snap.Nets.Count).Append(",");
            sb.Append("\"masked\":false},");

            sb.Append("\"current\":{");
            sb.Append("\"connected\":").Append(Bool(ci.Connected)).Append(",");
            sb.Append("\"ssid\":\"").Append(J(ci.Ssid)).Append("\",");
            sb.Append("\"bssid\":\"").Append(J(ci.Bssid)).Append("\",");
            sb.Append("\"signal\":").Append(ci.Signal).Append(",");
            sb.Append("\"rssi\":").Append(ci.HasRssi ? ci.Rssi.ToString(CultureInfo.InvariantCulture) : "null").Append(",");
            sb.Append("\"speed\":\"").Append(J(ci.Speed)).Append("\",");
            sb.Append("\"ip\":\"").Append(J(ci.Ip)).Append("\",");
            sb.Append("\"gateway\":\"").Append(J(ci.Gateway)).Append("\",");
            sb.Append("\"dns\":[");
            for (int i = 0; i < ci.Dns.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("\"").Append(J(ci.Dns[i])).Append("\"");
            }
            sb.Append("],");
            sb.Append("\"category\":\"\",");
            sb.Append("\"authCode\":\"");

            string authCode = "";
            for (int i = 0; i < snap.Nets.Count; i++)
            {
                if (snap.Nets[i].Current)
                {
                    authCode = snap.Nets[i].Auth;
                    break;
                }
            }
            sb.Append(J(authCode)).Append("\"},");

            sb.Append("\"nets\":[");
            for (int i = 0; i < snap.Nets.Count; i++)
            {
                WifiEntry n = snap.Nets[i];
                if (i > 0) sb.Append(",");
                sb.Append("{\"ssid\":\"").Append(J(n.Ssid)).Append("\",");
                sb.Append("\"auth\":\"").Append(J(n.Auth)).Append("\",");
                sb.Append("\"enc\":\"").Append(J(n.Enc)).Append("\",");
                sb.Append("\"key\":\"").Append(J(n.Key)).Append("\",");
                sb.Append("\"hasKey\":").Append(Bool(n.HasKey)).Append(",");
                sb.Append("\"mode\":\"").Append(J(n.Mode)).Append("\",");
                sb.Append("\"onex\":").Append(Bool(n.OneX)).Append(",");
                sb.Append("\"hidden\":").Append(Bool(n.Hidden)).Append(",");
                sb.Append("\"rand\":false,");
                sb.Append("\"cur\":").Append(Bool(n.Current)).Append("}");
            }
            sb.Append("]}");

            return sb.ToString();
        }

        /// <summary>Opens a local file with the shell default handler.</summary>
        public static void OpenWithShell(string path)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(path);
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch { }
        }
    }
}
