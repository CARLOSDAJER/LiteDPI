using System;
using System.Threading;

namespace LiteDPI
{
    /// <summary>
    /// Owns the mouse on a background thread. The UI says which DPI it wants; the worker applies it,
    /// reconnects after unplug/sleep, and re-applies. It never polls the DPI itself, so the mouse's
    /// own DPI buttons keep working until the next app switch.
    /// </summary>
    public sealed class DpiWorker : IDisposable
    {
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly Thread thread;
        volatile bool running = true, force, checkNow;
        volatile int desired;
        volatile int desiredColor = HidppDevice.ColorDevice;

        const int Unknown = int.MinValue;
        HidppDevice dev;
        int applied = -1, appliedColor = Unknown;

        // Read by the UI thread.
        public volatile string DeviceName;   // null = not connected
        public volatile DpiRange Range;
        public volatile int AppliedDpi;
        public volatile string LastError = "";
        public event Action Changed;          // raised on the worker thread

        public DpiWorker()
        {
            thread = new Thread(Loop) { IsBackground = true, Name = "LiteDPI mouse" };
            thread.Start();
        }

        public void Want(int dpi, int color) { desired = dpi; desiredColor = color; wake.Set(); }
        /// <summary>Apply again even if nothing changed (after sleep, or after "Try").</summary>
        public void Reapply() { force = true; wake.Set(); }
        /// <summary>Something was plugged/unplugged: check the connection now.</summary>
        public void CheckConnection() { checkNow = true; wake.Set(); }

        void Loop()
        {
            int lastCheck = Environment.TickCount;
            while (running)
            {
                wake.WaitOne(2000);
                if (!running) break;
                try
                {
                    if (dev == null)
                    {
                        dev = HidppDevice.Open();
                        if (dev == null)
                        {
                            SetStatus(null, "Mouse not found (" + HidppDevice.LastError + ")");
                            continue;
                        }
                        Range = dev.GetDpiRange();
                        applied = -1;
                        appliedColor = Unknown;
                        PrepareMode();
                        SetStatus(dev.Name, "");
                    }

                    int want = desired, wantColor = desiredColor;
                    if (force) { force = false; applied = -1; appliedColor = Unknown; }
                    bool talked = false;
                    if (want > 0 && want != applied)
                    {
                        int dpi = Range != null ? Range.Snap(want) : want;
                        dev.SetDpi(dpi);
                        applied = want;
                        AppliedDpi = dpi;
                        talked = true;
                        Raise();
                    }
                    if (wantColor != appliedColor)
                    {
                        if (dev.HasLighting) dev.SetLighting(wantColor);
                        appliedColor = wantColor;
                        talked = true;
                    }
                    if (talked) lastCheck = Environment.TickCount;
                    else if (checkNow || Environment.TickCount - lastCheck > 3000)
                    {
                        checkNow = false;
                        lastCheck = Environment.TickCount;
                        dev.Ping(); // throws if the mouse went away
                    }
                }
                catch (Exception e)
                {
                    if (dev != null) { try { dev.Dispose(); } catch { } dev = null; }
                    applied = -1;
                    appliedColor = Unknown;
                    SetStatus(null, e.Message);
                }
            }
            if (dev != null) { try { dev.Dispose(); } catch { } }
        }

        // G HUB leaves the mouse in "host mode", where the extra buttons only work while G HUB runs.
        // If G HUB isn't running, hand the buttons back to the mouse (onboard mode). Not saved to flash.
        void PrepareMode()
        {
            try
            {
                if (!GHub.IsRunning() && dev.GetOnboardMode() == 2) dev.SetOnboardMode(1);
            }
            catch { }
        }

        void SetStatus(string name, string error)
        {
            bool changed = DeviceName != name || LastError != error;
            DeviceName = name;
            LastError = error;
            if (name == null) AppliedDpi = 0;
            if (changed) Raise();
        }

        void Raise()
        {
            var h = Changed;
            if (h != null) h();
        }

        public void Dispose()
        {
            running = false;
            wake.Set();
            thread.Join(2000);
        }
    }

    public static class GHub
    {
        public static bool IsRunning()
        {
            foreach (var n in new[] { "lghub_agent", "lghub" })
            {
                var p = System.Diagnostics.Process.GetProcessesByName(n);
                bool any = p.Length > 0;
                foreach (var x in p) x.Dispose();
                if (any) return true;
            }
            return false;
        }
    }
}
