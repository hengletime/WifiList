using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WifiList
{
    internal static class Program
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

        [STAThread]
        private static void Main(string[] args)
        {
            // Must run before the first window is created: without it Windows
            // bitmap-stretches the whole UI on high-DPI displays.
            Dpi.Enable();

            // GitHub only speaks TLS 1.2+, which .NET 4.0 does not enable by default
            UpdateCheck.EnableTls();

            try { SetCurrentProcessExplicitAppUserModelID("Commanderc.WifiList." + Ui.Version); }
            catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // restore the last used palette and interface language before any
            // control is created, then rebuild the fonts for that script
            L.Load();
            Ui.SetTheme(Ui.SavedTheme());
            Ui.LoadFonts();

            if (args != null && args.Length >= 2 && string.Equals(args[0], "--render", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length >= 3)
                {
                    if (string.Equals(args[2], "light", StringComparison.OrdinalIgnoreCase)) Ui.SetTheme(UiTheme.Light);
                    else if (string.Equals(args[2], "dark", StringComparison.OrdinalIgnoreCase)) Ui.SetTheme(UiTheme.Dark);
                }
                if (args.Length >= 4) ApplyLanguageArg(args[3]);
                RenderPreview(args[1]);
                return;
            }

            // --about renders the About dialog for visual checks
            if (args != null && args.Length >= 2 && string.Equals(args[0], "--about", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length >= 3) ApplyLanguageArg(args[2]);
                RenderAbout(args[1]);
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--export", StringComparison.OrdinalIgnoreCase))
            {
                string path = args.Length >= 2 ? args[1] : HtmlExporter.DefaultPath();
                WifiSnapshot snap = WifiCollector.Load();
                HtmlExporter.Export(path, snap);
                return;
            }

            if (args != null && args.Length >= 2 && string.Equals(args[0], "--delete-profile", StringComparison.OrdinalIgnoreCase))
            {
                NetshResult r = WifiCollector.DeleteProfile(args[1]);
                Console.WriteLine((r.Ok ? "OK" : "FAIL") + " " + r.Code);
                Console.WriteLine(r.Text);
                Environment.ExitCode = r.Ok ? 0 : 1;
                return;
            }

            if (args != null && args.Length >= 3 && string.Equals(args[0], "--set-mode", StringComparison.OrdinalIgnoreCase))
            {
                bool auto = string.Equals(args[2], "auto", StringComparison.OrdinalIgnoreCase);
                NetshResult r = WifiCollector.SetConnectionMode(args[1], auto);
                Console.WriteLine((r.Ok ? "OK" : "FAIL") + " " + r.Code);
                Console.WriteLine(r.Text);
                Environment.ExitCode = r.Ok ? 0 : 1;
                return;
            }

            Application.Run(new MainForm());
        }


        /// <summary>Optional 4th argument of --render: zh | en | th.</summary>
        private static void ApplyLanguageArg(string arg)
        {
            if (arg == null) return;
            string v = arg.Trim().ToLowerInvariant();
            if (v == "en") L.Current = UiLang.En;
            else if (v == "th") L.Current = UiLang.Th;
            else if (v == "zh") L.Current = UiLang.Zh;
            else return;
            Ui.LoadFonts();
        }

        /// <summary>
        /// Renders the About dialog to a PNG and exits (visual checks only).
        /// </summary>
        private static void RenderAbout(string file)
        {
            try
            {
                using (AboutForm f = new AboutForm(WifiCollector.Load()))
                {
                    f.StartPosition = FormStartPosition.Manual;
                    f.Location = new Point(-4000, -4000);
                    f.ShowInTaskbar = false;
                    f.Show();
                    Application.DoEvents();
                    using (Bitmap bmp = new Bitmap(f.Width, f.Height))
                    {
                        f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                        bmp.Save(file, ImageFormat.Png);
                    }
                    f.Close();
                }
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(file + ".error.txt", ex.ToString()); } catch { }
            }
        }

        /// <summary>
        /// Renders the main window to a PNG and exits. Used for automated
        /// visual checks; never used in normal operation.
        /// </summary>
        private static void RenderPreview(string file)
        {
            try
            {
                using (MainForm f = new MainForm())
                {
                    f.StartPosition = FormStartPosition.Manual;
                    f.Location = new Point(-4000, -4000);
                    f.ShowInTaskbar = false;
                    f.Show();
                    Application.DoEvents();
                    using (Bitmap bmp = new Bitmap(f.Width, f.Height))
                    {
                        f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                        bmp.Save(file, ImageFormat.Png);
                    }
                    f.Close();
                }
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(file + ".error.txt", ex.ToString()); } catch { }
            }
        }
    }
}
