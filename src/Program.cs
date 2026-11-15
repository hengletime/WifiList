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

            try { SetCurrentProcessExplicitAppUserModelID("Commanderc.WifiList." + Ui.Version); }
            catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // restore the last used palette before any control is created
            Ui.SetTheme(Ui.SavedTheme());

            if (args != null && args.Length >= 2 && string.Equals(args[0], "--render", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length >= 3)
                {
                    if (string.Equals(args[2], "light", StringComparison.OrdinalIgnoreCase)) Ui.SetTheme(UiTheme.Light);
                    else if (string.Equals(args[2], "dark", StringComparison.OrdinalIgnoreCase)) Ui.SetTheme(UiTheme.Dark);
                }
                RenderPreview(args[1]);
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--export", StringComparison.OrdinalIgnoreCase))
            {
                string path = args.Length >= 2 ? args[1] : HtmlExporter.DefaultPath();
                WifiSnapshot snap = WifiCollector.Load();
                HtmlExporter.Export(path, snap);
                return;
            }

            Application.Run(new MainForm());
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
