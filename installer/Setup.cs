using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WifiListSetup
{
    /// <summary>
    /// Per-user installer for WifiList. It carries WifiList.exe as an embedded
    /// resource, so the download is one small file, nothing has to be fetched
    /// and no administrator rights are required for the default location.
    ///
    ///   Setup.exe                 interactive install
    ///   Setup.exe /S              silent install with defaults
    ///   Setup.exe --uninstall     interactive removal (also copied as uninstall.exe)
    ///   Setup.exe --uninstall /S  silent removal
    /// </summary>
    internal static class Program
    {
        public const string AppName = "WifiList";
        public const string AppTitle = "WiFi 密码清单";
        public const string AppVersion = "1.6.5";
        public const string Publisher = "Commanderc";
        public const string HomePage = "https://github.com/hengletime/WifiList";
        public const string PayloadName = "WifiList.Payload.exe";
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\WifiList";

        /// <summary>-1 = follow the system language; set by --lang for screenshots and tests.</summary>
        public static int ForceLang = -1;

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static void Main(string[] args)
        {
            try { SetProcessDPIAware(); }
            catch { }

            bool uninstall = false;
            bool silent = false;
            string render = "";
            bool renderRemove = false;
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string a = args[i] == null ? "" : args[i].ToLowerInvariant();
                    if (a == "--uninstall" || a == "/uninstall" || a == "-u") uninstall = true;
                    if (a == "/s" || a == "--silent" || a == "/silent") silent = true;
                    // hidden aid for maintenance: paint the window to a PNG without anyone watching
                    if (a == "--render" && i + 1 < args.Length) { render = args[i + 1]; renderRemove = false; }
                    if (a == "--render-remove" && i + 1 < args.Length) { render = args[i + 1]; renderRemove = true; }
                    if (a == "--lang" && i + 1 < args.Length)
                    {
                        string l = (args[i + 1] == null ? "" : args[i + 1].ToLowerInvariant());
                        ForceLang = l == "en" ? 1 : (l == "th" ? 2 : 0);
                    }
                }
            }

            // the installer leaves a copy of itself named uninstall.exe; running that copy
            // (double-click, Start Menu shortcut, Apps & features) means removal, no args needed
            if (!uninstall && IsUninstallCopy())
            {
                uninstall = true;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (render.Length > 0)
            {
                RenderTo(render, renderRemove);
                return;
            }

            if (silent)
            {
                int code = uninstall ? Installer.Remove(true) : Installer.Install(Installer.DefaultDir(), true, false, true);
                Environment.ExitCode = code;
                return;
            }

            if (uninstall)
            {
                using (SetupForm f = new SetupForm(true)) { Application.Run(f); }
            }
            else
            {
                using (SetupForm f = new SetupForm(false)) { Application.Run(f); }
            }
        }

        /// <summary>Paints the wizard to a PNG (offscreen) for layout checks without a human watching.</summary>
        private static void RenderTo(string path, bool uninstallMode)
        {
            try
            {
                using (SetupForm f = new SetupForm(uninstallMode))
                {
                    f.StartPosition = FormStartPosition.Manual;
                    f.Location = new Point(-4000, -4000);
                    f.Show();
                    Application.DoEvents();
                    using (Bitmap bmp = new Bitmap(f.Width, f.Height))
                    {
                        f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    f.Close();
                }
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(path + ".err.txt", ex.ToString()); }
                catch { }
            }
        }

        /// <summary>True when this process was started from a file named uninstall.exe.</summary>
        private static bool IsUninstallCopy()
        {
            try
            {
                string name = Path.GetFileNameWithoutExtension(Application.ExecutablePath);
                return string.Equals(name, "uninstall", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "uninst", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public static byte[] Payload()
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream s = asm.GetManifestResourceStream(PayloadName))
            {
                if (s == null) return null;
                using (MemoryStream ms = new MemoryStream())
                {
                    byte[] buf = new byte[81920];
                    int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
                    return ms.ToArray();
                }
            }
        }
    }

    /// <summary>All file and registry work. Kept apart from the UI so /S works headless.</summary>
    internal static class Installer
    {
        public static string DefaultDir()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(Path.Combine(appData, "Programs"), "WifiList");
        }

        public static string InstalledDir()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Program.UninstallKey))
                {
                    if (k == null) return "";
                    string loc = k.GetValue("InstallLocation") as string;
                    return loc == null ? "" : loc;
                }
            }
            catch { return ""; }
        }

        public static string InstalledVersion()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Program.UninstallKey))
                {
                    if (k == null) return "";
                    string v = k.GetValue("DisplayVersion") as string;
                    return v == null ? "" : v;
                }
            }
            catch { return ""; }
        }

        /// <summary>Kills a running copy so the payload can be replaced.</summary>
        public static bool CloseRunning()
        {
            bool any = false;
            Process[] list = Process.GetProcessesByName(Program.AppName);
            for (int i = 0; i < list.Length; i++)
            {
                try
                {
                    any = true;
                    list[i].CloseMainWindow();
                    if (!list[i].WaitForExit(3000)) list[i].Kill();
                }
                catch { }
                finally { try { list[i].Dispose(); } catch { } }
            }
            return any;
        }

        public static int Install(string dir, bool desktop, bool launch, bool silent)
        {
            try
            {
                byte[] payload = Program.Payload();
                if (payload == null) return 2;

                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string exe = Path.Combine(dir, Program.AppName + ".exe");
                File.WriteAllBytes(exe, payload);

                string self = Application.ExecutablePath;
                try
                {
                    string unins = Path.Combine(dir, "uninstall.exe");
                    if (!string.Equals(self, unins, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(self, unins, true);
                    }
                }
                catch { }

                string menuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Program.AppTitle);
                try
                {
                    if (!Directory.Exists(menuDir)) Directory.CreateDirectory(menuDir);
                    ShellLink.Create(Path.Combine(menuDir, Program.AppTitle + ".lnk"), exe, dir,
                        "WiFi 密码清单 v" + Program.AppVersion);
                    ShellLink.Create(Path.Combine(menuDir, "卸载 " + Program.AppTitle + ".lnk"),
                        Path.Combine(dir, "uninstall.exe"), dir, "卸载 WiFi 密码清单", "--uninstall");
                }
                catch { }

                if (desktop)
                {
                    try
                    {
                        string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                        ShellLink.Create(Path.Combine(desktopDir, Program.AppTitle + ".lnk"), exe, dir,
                            "WiFi 密码清单 v" + Program.AppVersion);
                    }
                    catch { }
                }
                else
                {
                    try
                    {
                        string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                        string lnk = Path.Combine(desktopDir, Program.AppTitle + ".lnk");
                        if (File.Exists(lnk)) File.Delete(lnk);
                    }
                    catch { }
                }

                WriteUninstallInfo(dir, exe, payload.Length);

                if (launch)
                {
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo(exe);
                        psi.WorkingDirectory = dir;
                        psi.UseShellExecute = true;
                        Process.Start(psi);
                    }
                    catch { }
                }
                return 0;
            }
            catch (Exception ex)
            {
                if (!silent) MessageBox.Show(ex.Message, "安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 3;
            }
        }

        public static int Remove(bool silent)
        {
            string dir = InstalledDir();
            if (dir.Length == 0) dir = DefaultDir();

            try
            {
                try
                {
                    string menuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Program.AppTitle);
                    if (Directory.Exists(menuDir)) Directory.Delete(menuDir, true);
                }
                catch { }

                try
                {
                    string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    string lnk = Path.Combine(desktopDir, Program.AppTitle + ".lnk");
                    if (File.Exists(lnk)) File.Delete(lnk);
                }
                catch { }

                try { Registry.CurrentUser.DeleteSubKeyTree(Program.UninstallKey, false); }
                catch { }

                // the running uninstall.exe locks itself, so it is removed on next boot
                string self = Application.ExecutablePath;
                string unins = Path.Combine(dir, "uninstall.exe");
                bool selfLocked = string.Equals(self, unins, StringComparison.OrdinalIgnoreCase);

                string[] files = new string[]
                {
                    Path.Combine(dir, Program.AppName + ".exe"),
                    Path.Combine(dir, "WifiList.pdb")
                };
                for (int i = 0; i < files.Length; i++)
                {
                    try { if (File.Exists(files[i])) File.Delete(files[i]); } catch { }
                }

                if (selfLocked)
                {
                    // a running exe cannot delete itself: schedule it for next boot,
                    // then hand the same job to a detached shell that waits for this
                    // process to exit, so the folder usually disappears right away
                    try { MoveFileEx(self, null, 4); } catch { }

                    // only wipe the whole folder when it is clearly ours
                    bool ownFolder = dir.IndexOf("WifiList", StringComparison.OrdinalIgnoreCase) >= 0;
                    string cmd = "/c ping -n 6 127.0.0.1 >nul & del /f /q \"" + self + "\" >nul 2>&1"
                               + " & ping -n 6 127.0.0.1 >nul & del /f /q \"" + self + "\" >nul 2>&1"
                               + (ownFolder ? " & rd /s /q \"" + dir + "\" >nul 2>&1" : "");
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", cmd);
                        psi.CreateNoWindow = true;
                        psi.UseShellExecute = false;
                        psi.WindowStyle = ProcessWindowStyle.Hidden;
                        Process.Start(psi);
                    }
                    catch { }
                }
                else
                {
                    try { if (File.Exists(unins)) File.Delete(unins); } catch { }
                }

                try
                {
                    if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0)
                    {
                        Directory.Delete(dir);
                    }
                }
                catch { }
                return 0;
            }
            catch (Exception ex)
            {
                if (!silent) MessageBox.Show(ex.Message, "卸载失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 3;
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool MoveFileEx(string existing, string target, int flags);

        private static void WriteUninstallInfo(string dir, string exe, int bytes)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Program.UninstallKey))
                {
                    if (k == null) return;
                    string unins = Path.Combine(dir, "uninstall.exe");
                    k.SetValue("DisplayName", "WiFi 密码清单 (WifiList)");
                    k.SetValue("DisplayVersion", Program.AppVersion);
                    k.SetValue("Publisher", Program.Publisher);
                    k.SetValue("DisplayIcon", exe + ",0");
                    k.SetValue("InstallLocation", dir);
                    k.SetValue("UninstallString", "\"" + unins + "\" --uninstall");
                    k.SetValue("QuietUninstallString", "\"" + unins + "\" --uninstall /S");
                    k.SetValue("URLInfoAbout", Program.HomePage);
                    k.SetValue("HelpLink", Program.HomePage);
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    k.SetValue("EstimatedSize", Math.Max(1, bytes / 1024), RegistryValueKind.DWord);
                    k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                }
            }
            catch { }
        }
    }

    /// <summary>Creates .lnk files through the WScript.Shell COM object.</summary>
    internal static class ShellLink
    {
        public static void Create(string path, string target, string workDir, string description) { Create(path, target, workDir, description, null); }

        public static void Create(string path, string target, string workDir, string description, string arguments)
        {
            object shell = null;
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return;
                shell = Activator.CreateInstance(t);
                object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                Type lt = lnk.GetType();
                lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { target });
                lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { workDir });
                lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { description });
                lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { target + ",0" });
                if (!string.IsNullOrEmpty(arguments))
                {
                    lt.InvokeMember("Arguments", BindingFlags.SetProperty, null, lnk, new object[] { arguments });
                }
                lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
            }
            catch { }
            finally
            {
                try
                {
                    if (shell != null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
                }
                catch { }
            }
        }
    }

    /// <summary>Three language string table for the installer itself.</summary>
    internal static class S
    {
        public static int Lang = 0;

        private static readonly Dictionary<string, string[]> Map = new Dictionary<string, string[]>(StringComparer.Ordinal);

        private static void A(string key, string zh, string en, string th)
        {
            Map[key] = new string[] { zh, en, th };
        }

        public static string T(string key)
        {
            string[] v;
            if (!Map.TryGetValue(key, out v)) return key;
            string s = v[Lang];
            return string.IsNullOrEmpty(s) ? v[0] : s;
        }

        public static string F(string key, params object[] args)
        {
            try { return string.Format(T(key), args); }
            catch { return T(key); }
        }

        static S()
        {
            A("win.install", "WiFi 密码清单 安装程序", "WiFi Password List Setup", "ตัวติดตั้งรายการรหัสผ่าน Wi-Fi");
            A("win.uninstall", "卸载 WiFi 密码清单", "Remove WiFi Password List", "ถอนการติดตั้งรายการรหัสผ่าน Wi-Fi");
            A("head.title", "WiFi 密码清单", "Wi-Fi Password List", "รายการรหัสผ่าน Wi-Fi");
            A("head.sub", "查看、连接与管理本机已保存的 Wi-Fi，并导出离线报告",
                "View, connect, manage saved Wi-Fi; export a report",
                "ดู เชื่อมต่อ และจัดการโปรไฟล์ Wi-Fi พร้อมส่งออกรายงานออฟไลน์");

            A("dir.label", "安装位置", "Install location", "ตำแหน่งติดตั้ง");
            A("dir.browse", "浏览…", "Browse…", "เลือก…");
            A("dir.hint", "默认装在当前用户目录，无需管理员权限；占用约 {0} MB。",
                "The default location is per user, so no administrator rights are needed. About {0} MB.",
                "ตำแหน่งเริ่มต้นอยู่ในโฟลเดอร์ผู้ใช้ จึงไม่ต้องใช้สิทธิ์ผู้ดูแล ใช้พื้นที่ราว {0} MB");
            A("dir.upgrade", "检测到已安装 v{0}，将覆盖安装并保留原有设置。",
                "Version {0} is already installed; it will be upgraded and your settings kept.",
                "พบเวอร์ชัน {0} ติดตั้งอยู่ ระบบจะอัปเกรดและเก็บการตั้งค่าเดิมไว้");
            A("dir.labelRemove", "安装位置（将从此处卸载）", "Installed at (removed from here)",
                "ตำแหน่งที่ติดตั้ง (จะถอนจากที่นี่)");
            A("dir.hintRemove", "程序文件、开始菜单项和桌面快捷方式都会被删除；导出的报告不受影响。",
                "Program files, the Start menu entry and the desktop shortcut will be deleted. Exported reports are left alone.",
                "ไฟล์โปรแกรม รายการเมนูเริ่ม และทางลัดเดสก์ท็อปจะถูกลบ แต่รายงานที่ส่งออกไว้จะยังอยู่");

            A("opt.desktop", "创建桌面快捷方式", "Create a desktop shortcut", "สร้างทางลัดบนเดสก์ท็อป");
            A("opt.launch", "安装完成后立即运行", "Run the app after installing", "เปิดโปรแกรมหลังติดตั้งเสร็จ");

            A("btn.install", "安装", "Install", "ติดตั้ง");
            A("btn.remove", "卸载", "Remove", "ถอนการติดตั้ง");
            A("btn.cancel", "取消", "Cancel", "ยกเลิก");
            A("btn.close", "关闭", "Close", "ปิด");

            A("msg.working", "正在安装，请稍候…", "Installing, please wait…", "กำลังติดตั้ง โปรดรอสักครู่…");
            A("msg.done", "安装完成，已创建开始菜单快捷方式。", "Installation complete. A Start menu shortcut was created.",
                "ติดตั้งเสร็จแล้ว และสร้างทางลัดในเมนูเริ่มให้เรียบร้อย");
            A("msg.doneNoMenu", "安装完成。", "Installation complete.", "ติดตั้งเสร็จแล้ว");
            A("msg.doneRun", "安装完成，程序已启动，本窗口即将关闭。",
                "Installed. The app is starting — this window will close.",
                "ติดตั้งเสร็จแล้ว กำลังเปิดโปรแกรม หน้าต่างนี้จะปิดเอง");
            A("msg.failed", "安装失败：", "Installation failed: ", "ติดตั้งไม่สำเร็จ: ");
            A("msg.noPayload", "安装包已损坏（缺少程序文件），请重新下载。",
                "The package is damaged (program file missing). Please download it again.",
                "แพ็กเกจเสียหาย (ไม่พบไฟล์โปรแกรม) โปรดดาวน์โหลดใหม่");
            A("msg.badDir", "请选择一个有效的安装位置。", "Please choose a valid install folder.",
                "โปรดเลือกโฟลเดอร์ติดตั้งที่ถูกต้อง");
            A("msg.needClose", "程序正在运行，将先关闭它以完成安装。继续吗？",
                "The app is running and will be closed to continue. Proceed?",
                "โปรแกรมกำลังทำงาน ต้องปิดก่อนติดตั้ง ดำเนินการต่อหรือไม่");
            A("msg.askRemove", "确定要卸载 WiFi 密码清单吗？\r\n\r\n快捷方式、开始菜单项和程序文件都会被删除，导出的报告不会受影响。",
                "Remove WiFi Password List?\r\n\r\nShortcuts, the Start menu entry and the program files will be deleted. Exported reports are left alone.",
                "ถอนการติดตั้งรายการรหัสผ่าน Wi-Fi หรือไม่?\r\n\r\nทางลัด รายการในเมนูเริ่ม และไฟล์โปรแกรมจะถูกลบ แต่รายงานที่ส่งออกไว้จะยังอยู่");
            A("msg.removed", "已卸载，程序文件已删除。", "Removed. The program files are gone.",
                "ถอนการติดตั้งแล้ว ไฟล์โปรแกรมถูกลบแล้ว");
            A("msg.notInstalled", "没有找到已安装的 WiFi 密码清单。", "WiFi Password List is not installed.",
                "ไม่พบรายการรหัสผ่าน Wi-Fi ที่ติดตั้งไว้");
            A("msg.running", "请先关闭程序再安装。", "Please close the app before installing.",
                "โปรดปิดโปรแกรมก่อนติดตั้ง");
            A("msg.cannotWrite", "无法写入该目录：{0}\r\n\r\n可以换到默认位置，或以管理员身份运行。",
                "Cannot write to {0}\r\n\r\nUse the default folder, or run as administrator.",
                "เขียนไปยัง {0} ไม่ได้\r\n\r\nลองใช้โฟลเดอร์เริ่มต้น หรือเรียกใช้ในฐานะผู้ดูแลระบบ");
        }
    }

    /// <summary>The wizard window: path, two options, one button.</summary>
    internal sealed class SetupForm : Form
    {
        private const int Pad = 26;
        private bool _uninstallMode;
        private bool _busy;
        private bool _finished;

        private TextBox _txtDir;
        private Button _btnBrowse;
        private CheckBox _chkDesktop;
        private CheckBox _chkLaunch;
        private Button _btnMain;
        private Button _btnCancel;
        private Label _lblDir;
        private Label _lblHint;
        private Label _lblStatus;
        private Button[] _langs;
        private System.Windows.Forms.Timer _autoClose;

        private int _headerH;

        public SetupForm(bool uninstall)
        {
            _uninstallMode = uninstall;

            string lang = "zh";
            try
            {
                string n = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                if (n == "en" || n == "th") lang = n;
            }
            catch { }
            S.Lang = Program.ForceLang >= 0 ? Program.ForceLang
                : (lang == "en" ? 1 : (lang == "th" ? 2 : 0));

            this.Font = new Font("Microsoft YaHei UI", 9F);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.White;
            this.ClientSize = new Size(Z(560), Z(392));
            this.AutoScaleMode = AutoScaleMode.None;

            _headerH = Z(86);

            _lblDir = new Label();
            _lblDir.AutoSize = true;
            this.Controls.Add(_lblDir);

            _txtDir = new TextBox();
            _txtDir.BorderStyle = BorderStyle.FixedSingle;
            this.Controls.Add(_txtDir);

            _btnBrowse = new Button();
            _btnBrowse.FlatStyle = FlatStyle.System;
            _btnBrowse.Click += delegate { Browse(); };
            this.Controls.Add(_btnBrowse);

            _lblHint = new Label();
            _lblHint.AutoSize = false;
            _lblHint.ForeColor = Color.FromArgb(107, 122, 140);
            this.Controls.Add(_lblHint);

            _chkDesktop = new CheckBox();
            _chkDesktop.AutoSize = true;
            _chkDesktop.Checked = true;
            this.Controls.Add(_chkDesktop);

            _chkLaunch = new CheckBox();
            _chkLaunch.AutoSize = true;
            _chkLaunch.Checked = true;
            this.Controls.Add(_chkLaunch);

            _lblStatus = new Label();
            _lblStatus.AutoSize = false;
            _lblStatus.ForeColor = Color.FromArgb(58, 120, 180);
            this.Controls.Add(_lblStatus);

            _btnMain = new Button();
            _btnMain.FlatStyle = FlatStyle.System;
            _btnMain.Click += delegate { Go(); };
            this.Controls.Add(_btnMain);

            _btnCancel = new Button();
            _btnCancel.FlatStyle = FlatStyle.System;
            _btnCancel.Click += delegate { this.Close(); };
            this.Controls.Add(_btnCancel);

            string[] names = new string[] { "中文", "English", "ไทย" };
            _langs = new Button[3];
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                Button b = new Button();
                b.Text = names[i];
                b.FlatStyle = FlatStyle.System;
                b.Click += delegate { SwitchLang(idx); };
                this.Controls.Add(b);
                _langs[i] = b;
            }

            this.AcceptButton = _btnMain;
            this.CancelButton = _btnCancel;

            ApplyLanguage();
            Prefill();
            if (_uninstallMode)
            {
                _txtDir.Enabled = false;
                _btnBrowse.Visible = false;
                _chkDesktop.Visible = false;
                _chkLaunch.Visible = false;
            }
            LayoutAll();
        }

        private static int Z(int v)
        {
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    return (int)Math.Round(v * (g.DpiX / 96.0));
                }
            }
            catch { return v; }
        }

        /// <summary>Thai needs Leelawadee UI, Chinese needs YaHei; Segoe UI covers English.</summary>
        private static string FamilyFor(int lang)
        {
            if (lang == 2) return "Leelawadee UI";
            if (lang == 1) return "Segoe UI";
            return "Microsoft YaHei UI";
        }

        private void SwitchLang(int idx)
        {
            if (S.Lang == idx) return;
            S.Lang = idx;
            ApplyLanguage();
            LayoutAll();
        }

        private void ApplyLanguage()
        {
            this.Font = new Font(FamilyFor(S.Lang), 9F);
            this.Text = _uninstallMode ? S.T("win.uninstall") : S.T("win.install");
            _lblDir.Text = _uninstallMode ? S.T("dir.labelRemove") : S.T("dir.label");
            _btnBrowse.Text = S.T("dir.browse");
            _chkDesktop.Text = S.T("opt.desktop");
            _chkLaunch.Text = S.T("opt.launch");
            _btnMain.Text = _finished ? S.T("btn.close")
                : (_uninstallMode ? S.T("btn.remove") : S.T("btn.install"));
            _btnCancel.Text = S.T("btn.cancel");
            if (!_finished && !_busy) _lblStatus.Text = "";

            _chkDesktop.Visible = !_uninstallMode && !_finished;
            _chkLaunch.Visible = !_uninstallMode && !_finished;

            double mb = 0.2;
            try
            {
                byte[] p = Program.Payload();
                if (p != null) mb = Math.Round(p.Length / 1048576.0, 1);
            }
            catch { }
            _lblHint.Text = _uninstallMode
                ? S.T("dir.hintRemove")
                : S.F("dir.hint", mb.ToString("0.0"));

            string installed = Installer.InstalledVersion();
            if (!_uninstallMode && installed.Length > 0 && installed != Program.AppVersion)
            {
                _lblHint.Text = S.F("dir.upgrade", installed) + "\r\n" + _lblHint.Text;
            }
            this.Invalidate();
        }

        private void Prefill()
        {
            string dir = Installer.InstalledDir();
            if (dir.Length == 0) dir = Installer.DefaultDir();
            _txtDir.Text = dir;
        }

        private void Browse()
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = S.T("dir.label");
                d.SelectedPath = _txtDir.Text;
                if (d.ShowDialog(this) == DialogResult.OK) _txtDir.Text = d.SelectedPath;
            }
        }

        private void LayoutAll()
        {
            int pad = Z(Pad);
            int w = this.ClientSize.Width;

            int y = _headerH + Z(22);
            _lblDir.Location = new Point(pad, y);
            y += Z(24);

            int browseW = _uninstallMode
                ? 0
                : Math.Max(Z(78), TextRenderer.MeasureText(_btnBrowse.Text, this.Font).Width + Z(24));
            _btnBrowse.Visible = !_uninstallMode;
            _txtDir.SetBounds(pad, y, w - pad * 2 - (browseW > 0 ? browseW + Z(8) : 0), Z(26));
            if (browseW > 0) _btnBrowse.SetBounds(w - pad - browseW, y - Z(1), browseW, Z(28));
            else _btnBrowse.SetBounds(-2000, -2000, browseW, 0);
            y += Z(34);

            int hintW = w - pad * 2;
            int hintH = Math.Max(Z(38),
                TextRenderer.MeasureText(_lblHint.Text, this.Font, new Size(hintW, Z(400)),
                    TextFormatFlags.WordBreak).Height + Z(8));
            _lblHint.SetBounds(pad, y, hintW, hintH);
            y += hintH + Z(4);

            _chkDesktop.Location = new Point(pad, y);
            y += Z(30);
            _chkLaunch.Location = new Point(pad, y);
            y += Z(34);

            _lblStatus.SetBounds(pad, y, w - pad * 2, Z(40));

            int btnH = Z(30);
            int btnY = this.ClientSize.Height - Z(16) - btnH;
            int rx = w - pad;

            int wCancel = Math.Max(Z(84), TextRenderer.MeasureText(_btnCancel.Text, this.Font).Width + Z(28));
            _btnCancel.SetBounds(rx - wCancel, btnY, wCancel, btnH);
            rx -= wCancel + Z(8);

            int wMain = Math.Max(Z(96), TextRenderer.MeasureText(_btnMain.Text, this.Font).Width + Z(30));
            _btnMain.SetBounds(rx - wMain, btnY, wMain, btnH);

            int lx = pad;
            for (int i = 0; i < _langs.Length; i++)
            {
                int lw = Math.Max(Z(58), TextRenderer.MeasureText(_langs[i].Text, this.Font).Width + Z(18));
                _langs[i].SetBounds(lx, btnY, lw, btnH);
                lx += lw + Z(6);
            }
        }

        /// <summary>Closes the wizard after a short pause so the last status stays readable.</summary>
        private void AutoClose(int ms)
        {
            if (_autoClose == null)
            {
                _autoClose = new System.Windows.Forms.Timer();
                _autoClose.Tick += delegate
                {
                    _autoClose.Stop();
                    this.Close();
                };
            }
            _autoClose.Stop();
            _autoClose.Interval = ms;
            _autoClose.Start();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // real client size is only reliable once the window exists; lay out again so
            // nothing keeps a default (0,0) position left over from the constructor
            LayoutAll();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            int pad = Z(Pad);
            int w = this.ClientSize.Width;

            Rectangle head = new Rectangle(0, 0, w, _headerH);
            using (LinearGradientBrush b = new LinearGradientBrush(head,
                Color.FromArgb(9, 17, 26), Color.FromArgb(16, 38, 54), 12F))
            {
                g.FillRectangle(b, head);
            }
            using (Pen p = new Pen(Color.FromArgb(56, 189, 248), Z(2)))
            {
                g.DrawLine(p, 0, _headerH - Z(2), w, _headerH - Z(2));
            }

            int logo = Z(42);
            int lx = pad;
            int ly = (_headerH - logo) / 2 - Z(2);
            try
            {
                using (Icon ic = LoadAppIcon())
                {
                    if (ic != null) g.DrawIcon(ic, new Rectangle(lx, ly, logo, logo));
                }
            }
            catch { }

            int tx = lx + logo + Z(14);
            using (Font f = new Font(FamilyFor(S.Lang), 13F, FontStyle.Bold))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(233, 240, 248)))
            {
                g.DrawString(S.T("head.title"), f, b, tx, ly - Z(2));
            }
            using (Font f = new Font(FamilyFor(S.Lang), 8.5F))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(157, 176, 196)))
            {
                string ver = "v" + Program.AppVersion;
                SizeF vs = g.MeasureString(ver, f);
                g.DrawString(ver, f, b, w - pad - vs.Width, ly + Z(24));

                // the Thai subtitle is long: clip it instead of running under the version
                Rectangle sub = new Rectangle(tx, ly + Z(24),
                    Math.Max(Z(60), (int)(w - pad - vs.Width - Z(10) - tx)), Z(18));
                using (StringFormat sf = new StringFormat())
                {
                    sf.Trimming = StringTrimming.EllipsisCharacter;
                    sf.FormatFlags = StringFormatFlags.NoWrap;
                    g.DrawString(S.T("head.sub"), f, b, sub, sf);
                }
            }
        }

        private static Icon LoadAppIcon()
        {
            try
            {
                return Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch { return null; }
        }

        private void Go()
        {
            if (_busy) return;

            if (_finished)
            {
                this.Close();
                return;
            }

            string dir = _txtDir.Text == null ? "" : _txtDir.Text.Trim();
            if (dir.Length == 0)
            {
                MessageBox.Show(this, S.T("msg.badDir"), this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_uninstallMode)
            {
                if (MessageBox.Show(this, S.T("msg.askRemove"), this.Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

                _busy = true;
                _lblStatus.Text = S.T("msg.working");
                Application.DoEvents();

                Installer.CloseRunning();
                int rc = Installer.Remove(false);

                _busy = false;
                if (rc == 0)
                {
                    _finished = true;
                    _lblStatus.Text = S.T("msg.removed");
                    _btnCancel.Visible = false;
                    ApplyLanguage();
                    LayoutAll();
                    // leaving early lets the detached shell delete this exe and the folder
                    AutoClose(2200);
                }
                else
                {
                    _lblStatus.Text = S.T("msg.failed");
                }
                return;
            }

            if (Program.Payload() == null)
            {
                MessageBox.Show(this, S.T("msg.noPayload"), this.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (Directory.Exists(dir) == false)
            {
                try { Directory.CreateDirectory(dir); }
                catch
                {
                    MessageBox.Show(this, S.F("msg.cannotWrite", dir), this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            // probe writability before touching anything
            try
            {
                string probe = Path.Combine(dir, ".write-test");
                File.WriteAllText(probe, "x");
                File.Delete(probe);
            }
            catch
            {
                MessageBox.Show(this, S.F("msg.cannotWrite", dir), this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (Process.GetProcessesByName(Program.AppName).Length > 0)
            {
                if (MessageBox.Show(this, S.T("msg.needClose"), this.Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                Installer.CloseRunning();
                Thread.Sleep(400);
            }

            _busy = true;
            _lblStatus.Text = S.T("msg.working");
            Application.DoEvents();

            int code = Installer.Install(dir, _chkDesktop.Checked, _chkLaunch.Checked, false);

            _busy = false;
            if (code == 0)
            {
                _finished = true;
                bool ran = _chkLaunch.Checked;
                _lblStatus.Text = ran ? S.T("msg.doneRun") : S.T("msg.done");
                _chkDesktop.Visible = false;
                _chkLaunch.Visible = false;
                _txtDir.Enabled = false;
                _btnBrowse.Enabled = false;
                _btnCancel.Visible = false;
                ApplyLanguage();
                LayoutAll();
                // the app is already up and showing itself: get out of the way
                if (ran) AutoClose(1000);
            }
            else
            {
                _lblStatus.Text = S.T("msg.failed");
            }
        }
    }
}
