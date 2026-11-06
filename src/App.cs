using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LiteDPI
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool first;
            using (var mutex = new Mutex(true, "LiteDPI.SingleInstance", out first))
            {
                if (!first)
                {
                    MessageBox.Show("LiteDPI is already running. Look for its icon in the system tray.", "LiteDPI");
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
            }
        }
    }

    /// <summary>Invisible top-level window: gives us a UI-thread invoker plus device and power broadcasts.</summary>
    class HiddenWindow : Form
    {
        public event Action DeviceChanged, Resumed;
        protected override void SetVisibleCore(bool value) { base.SetVisibleCore(false); }
        protected override void WndProc(ref Message m)
        {
            const int WM_DEVICECHANGE = 0x0219, WM_POWERBROADCAST = 0x0218;
            if (m.Msg == WM_DEVICECHANGE && DeviceChanged != null) DeviceChanged();
            if (m.Msg == WM_POWERBROADCAST && Resumed != null)
            {
                long ev = m.WParam.ToInt64();
                if (ev == 0x7 || ev == 0x12) Resumed(); // PBT_APMRESUMESUSPEND / PBT_APMRESUMEAUTOMATIC
            }
            base.WndProc(ref m);
        }
    }

    /// <summary>"Start with Windows" = a value under HKCU\...\Run. Per user, no admin needed.</summary>
    static class Startup
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool IsEnabled()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue("LiteDPI") != null;
        }

        public static void Set(bool on)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue("LiteDPI", "\"" + Application.ExecutablePath + "\"");
                else k.DeleteValue("LiteDPI", false);
            }
        }
    }

    class TrayApp : ApplicationContext
    {
        readonly NotifyIcon tray = new NotifyIcon();
        readonly HiddenWindow window = new HiddenWindow();
        readonly DpiWorker worker = new DpiWorker();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer reloadTimer = new System.Windows.Forms.Timer();
        readonly FileSystemWatcher watcher;
        readonly ToolStripMenuItem statusItem, deviceItem, ghubItem, pauseItem, startupItem;
        readonly WinEventDelegate hookProc; // keep a reference so the GC doesn't collect it
        readonly IntPtr hook;

        Config config;
        SettingsForm settings;
        IntPtr lastHwnd;
        string currentLabel = "Desktop";
        int currentWant;
        bool paused, ghubWarned;
        int timerTicks;
        Icon currentIcon;

        public TrayApp()
        {
            config = Config.Load();
            IntPtr force = window.Handle; // create the window now

            var menu = new ContextMenuStrip();
            statusItem = new ToolStripMenuItem("Starting...") { Enabled = false };
            statusItem.Font = new Font(menu.Font, FontStyle.Bold);
            deviceItem = new ToolStripMenuItem("Looking for the mouse...") { Enabled = false };
            ghubItem = new ToolStripMenuItem("G HUB is running and will override LiteDPI. Quit G HUB from its tray icon.") { Enabled = false, Visible = false };
            pauseItem = new ToolStripMenuItem("Pause switching", null, delegate { paused = !paused; pauseItem.Checked = paused; Evaluate(true); });
            startupItem = new ToolStripMenuItem("Start with Windows", null, delegate
            {
                Startup.Set(!Startup.IsEnabled());
                startupItem.Checked = Startup.IsEnabled();
            });
            startupItem.Checked = Startup.IsEnabled();
            menu.Opening += delegate { startupItem.Checked = Startup.IsEnabled(); };
            menu.Items.Add(statusItem);
            menu.Items.Add(deviceItem);
            menu.Items.Add(ghubItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Settings...", null, delegate { ShowSettings(); }));
            menu.Items.Add(pauseItem);
            menu.Items.Add(startupItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("LiteDPI v" + AppInfo.Version) { Enabled = false });
            menu.Items.Add(new ToolStripMenuItem("Exit", null, delegate { ExitThread(); }));

            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { ShowSettings(); };
            tray.Visible = true;

            worker.Changed += delegate { if (window.IsHandleCreated) window.BeginInvoke((Action)UpdateTray); };
            window.DeviceChanged += delegate { worker.CheckConnection(); };
            window.Resumed += delegate { worker.Reapply(); };

            hookProc = OnForegroundChanged;
            hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, hookProc, 0, 0, WINEVENT_OUTOFCONTEXT);

            timer.Interval = 1000; // safety net in case a foreground event is missed
            timer.Tick += delegate
            {
                Evaluate(false);
                if (++timerTicks % 5 == 1) CheckGHub();
            };
            timer.Start();

            // Pick up hand edits to LiteDPI.ini without a restart.
            watcher = new FileSystemWatcher(Path.GetDirectoryName(Config.FilePath), Path.GetFileName(Config.FilePath));
            watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName;
            FileSystemEventHandler onFile = delegate { window.BeginInvoke((Action)delegate { reloadTimer.Stop(); reloadTimer.Start(); }); };
            watcher.Changed += onFile;
            watcher.Created += onFile;
            watcher.Renamed += delegate { window.BeginInvoke((Action)delegate { reloadTimer.Stop(); reloadTimer.Start(); }); };
            watcher.EnableRaisingEvents = true;
            reloadTimer.Interval = 300; // editors write in bursts; wait for the last one
            reloadTimer.Tick += delegate
            {
                reloadTimer.Stop();
                try { ReloadConfig(Config.Load()); } catch { }
            };

            Evaluate(true);
            CheckGHub();
            UpdateTray();
        }

        // ---------- foreground tracking ----------

        void OnForegroundChanged(IntPtr h, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time)
        {
            Evaluate(false);
        }

        /// <summary>Works out which DPI the program in front wants and passes it to the worker.</summary>
        void Evaluate(bool always)
        {
            IntPtr hwnd = GetForegroundWindow();
            if (!always && hwnd == lastHwnd) return;
            if (hwnd == IntPtr.Zero) return; // focus in limbo (e.g. mid alt-tab): keep what we have
            lastHwnd = hwnd;

            string path, name;
            GetProcessOf(hwnd, out path, out name);
            Rule r = config.Find(path, name);
            currentLabel = r != null ? (name ?? r.Match) : "Desktop";
            currentWant = r != null ? r.Dpi : config.DesktopDpi;
            if (!paused) worker.Want(currentWant);
            UpdateTray();
        }

        public void ReloadConfig(Config c)
        {
            config = c;
            Evaluate(true);
            worker.Reapply();
        }

        /// <summary>Settings window "Try it" button.</summary>
        public void TryDpi(int dpi)
        {
            worker.Want(dpi);
            worker.Reapply();
        }

        public DpiWorker Worker { get { return worker; } }

        static void GetProcessOf(IntPtr hwnd, out string path, out string name)
        {
            path = null; name = null;
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            if (pid == 0) return;
            IntPtr hp = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hp != IntPtr.Zero)
            {
                try
                {
                    var sb = new StringBuilder(1024);
                    int size = sb.Capacity;
                    if (QueryFullProcessImageName(hp, 0, sb, ref size)) path = sb.ToString(0, size);
                }
                finally { CloseHandle(hp); }
            }
            if (path != null) { name = Path.GetFileName(path); return; }
            // Some protected (anti-cheat) processes refuse the query: fall back to the bare name.
            try { using (var p = Process.GetProcessById((int)pid)) name = p.ProcessName + ".exe"; } catch { }
        }

        // ---------- tray ----------

        void UpdateTray()
        {
            string device = worker.DeviceName;
            int dpi = worker.AppliedDpi;
            string text;
            Color color;
            string badge;

            if (device == null)
            {
                text = "LiteDPI - mouse not found";
                color = Color.FromArgb(107, 114, 128);
                badge = "--";
                deviceItem.Text = "Mouse not found" + (worker.LastError.Length > 0 ? ": " + worker.LastError : "");
            }
            else
            {
                deviceItem.Text = device;
                badge = Badge(dpi > 0 ? dpi : currentWant);
                if (paused) { text = "LiteDPI - paused at " + dpi + " DPI"; color = Color.FromArgb(107, 114, 128); }
                else
                {
                    text = "LiteDPI - " + (dpi > 0 ? dpi : currentWant) + " DPI - " + currentLabel;
                    color = currentLabel == "Desktop" ? Color.FromArgb(37, 99, 235) : Color.FromArgb(22, 163, 74);
                }
            }
            statusItem.Text = paused ? "Paused" : (device == null ? "Waiting for the mouse" : (dpi > 0 ? dpi : currentWant) + " DPI  -  " + currentLabel);
            tray.Text = text.Length > 63 ? text.Substring(0, 63) : text;

            Icon old = currentIcon;
            currentIcon = MakeIcon(badge, color);
            tray.Icon = currentIcon;
            if (old != null) old.Dispose();
        }

        static string Badge(int dpi)
        {
            if (dpi <= 0) return "--";
            if (dpi >= 10000 || dpi % 1000 == 0) return (dpi / 1000) + "k";
            return (dpi / 1000.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        }

        static Icon MakeIcon(string text, Color bg)
        {
            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);
                using (var path = RoundedRect(new Rectangle(0, 0, 31, 31), 7))
                using (var b = new SolidBrush(bg))
                    g.FillPath(b, path);
                float size = text.Length >= 3 ? 14f : 17f;
                using (var f = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(text, f, Brushes.White, new RectangleF(0, 1, 32, 32), sf);
                IntPtr h = bmp.GetHicon();
                try { return (Icon)Icon.FromHandle(h).Clone(); }
                finally { DestroyIcon(h); }
            }
        }

        static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        void CheckGHub()
        {
            bool running = GHub.IsRunning();
            ghubItem.Visible = running;
            if (running && !ghubWarned)
            {
                ghubWarned = true;
                tray.ShowBalloonTip(8000, "G HUB is still running",
                    "G HUB will keep overriding your DPI. Quit it from its tray icon, and turn off \"Start on boot\" in G HUB settings.",
                    ToolTipIcon.Warning);
            }
            if (!running && ghubWarned)
            {
                ghubWarned = false;
                worker.Reapply(); // G HUB may have changed the DPI on its way out
            }
        }

        void ShowSettings()
        {
            if (settings != null && !settings.IsDisposed) { settings.Activate(); return; }
            settings = new SettingsForm(this, config);
            settings.FormClosed += delegate { settings = null; };
            settings.Show();
            settings.Activate();
        }

        protected override void ExitThreadCore()
        {
            timer.Stop();
            reloadTimer.Stop();
            watcher.Dispose();
            if (hook != IntPtr.Zero) UnhookWinEvent(hook);
            tray.Visible = false;
            tray.Dispose();
            worker.Dispose();
            window.Dispose();
            base.ExitThreadCore();
        }

        // ---------- Win32 ----------

        const uint EVENT_SYSTEM_FOREGROUND = 3, WINEVENT_OUTOFCONTEXT = 0, PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        delegate void WinEventDelegate(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr hmod, WinEventDelegate cb, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder sb, ref int size);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    }
}
