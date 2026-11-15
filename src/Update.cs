using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace WifiList
{
    /// <summary>Facts about the newest published release.</summary>
    internal sealed class ReleaseInfo
    {
        public Version Version;
        public string Tag = "";
        public string Url = "";
        public string Name = "";
    }

    /// <summary>
    /// Looks up the latest GitHub release. Everything is best effort: the
    /// application must keep working with no network at all, so every failure
    /// path returns null and the caller simply stays quiet.
    /// </summary>
    internal static class UpdateCheck
    {
        public const string ApiLatest = "https://api.github.com/repos/hengletime/WifiList/releases/latest";
        public const string ReleasesPage = "https://github.com/hengletime/WifiList/releases/latest";

        /// <summary>
        /// GitHub refuses TLS 1.0, which is what .NET 4.0 negotiates by default.
        /// 3072 is Tls12; the cast avoids depending on a newer enum member.
        /// </summary>
        public static void EnableTls()
        {
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            }
            catch { }
        }

        /// <summary>Returns the newest release, or null when it cannot be read.</summary>
        public static ReleaseInfo Latest(int timeoutMs)
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(ApiLatest);
                req.UserAgent = "WifiList/" + Ui.Version;
                req.Accept = "application/vnd.github+json";
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                req.AllowAutoRedirect = true;
                try
                {
                    req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                }
                catch { }

                using (WebResponse resp = req.GetResponse())
                {
                    using (Stream s = resp.GetResponseStream())
                    {
                        if (s == null) return null;
                        using (StreamReader reader = new StreamReader(s, Encoding.UTF8))
                        {
                            return Parse(reader.ReadToEnd());
                        }
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Pulls tag_name / html_url out of the release JSON.</summary>
        private static ReleaseInfo Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;

            string tag = Field(json, "tag_name");
            if (tag.Length == 0) tag = Field(json, "name");
            Version v = ParseVersion(tag);
            if (v == null) return null;

            ReleaseInfo r = new ReleaseInfo();
            r.Tag = tag;
            r.Version = v;
            r.Url = Field(json, "html_url");
            r.Name = Field(json, "name");
            if (r.Url.Length == 0) r.Url = ReleasesPage;
            return r;
        }

        private static string Field(string json, string key)
        {
            Match m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (!m.Success) return "";
            return Regex.Replace(m.Groups[1].Value, "\\\\(.)", "$1");
        }

        /// <summary>Turns "v1.6.0" or "1.6" into a comparable Version.</summary>
        public static Version ParseVersion(string text)
        {
            if (text == null) return null;
            Match m = Regex.Match(text, "(\\d+)\\.(\\d+)(?:\\.(\\d+))?");
            if (!m.Success) return null;
            int major = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            int minor = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            int build = m.Groups[3].Success ? int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
            return new Version(major, minor, build);
        }

        /// <summary>True when <paramref name="remote"/> is newer than the running build.</summary>
        public static bool IsNewer(Version remote)
        {
            if (remote == null) return false;
            Version local = ParseVersion(Ui.Version);
            if (local == null) return false;
            return remote > local;
        }
    }
}
