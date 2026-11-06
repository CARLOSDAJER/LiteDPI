// Console diagnostic: litedpi-probe            -> show mouse info
//                     litedpi-probe set 1600   -> set DPI
using System;

namespace LiteDPI
{
    static class Probe
    {
        static int Main(string[] args)
        {
            Console.WriteLine("LiteDPI probe v" + AppInfo.Version);
            Console.WriteLine("Logitech HID collections:");
            foreach (var i in Hid.Enumerate(0x046D)) Console.WriteLine("  " + i);

            using (var d = HidppDevice.Open())
            {
                if (d == null) { Console.WriteLine("No HID++ mouse with adjustable DPI found. Last error: " + HidppDevice.LastError); return 1; }
                Console.WriteLine("Mouse:        " + d.Name + "  (device index 0x" + d.DeviceIndex.ToString("X2") + ")");
                Console.WriteLine("Current DPI:  " + d.GetDpi());
                Console.WriteLine("Supported:    " + d.GetDpiRange());
                int mode = d.GetOnboardMode();
                Console.WriteLine("Memory mode:  " + (mode == 1 ? "onboard" : mode == 2 ? "host (software)" : "n/a (" + mode + ")"));

                if (args.Length == 2 && args[0] == "mode")
                {
                    d.SetOnboardMode(args[1] == "onboard" ? 1 : 2);
                    Console.WriteLine("Mode now: " + d.GetOnboardMode());
                }

                if (args.Length == 2 && args[0] == "set")
                {
                    int want = d.GetDpiRange().Snap(int.Parse(args[1]));
                    d.SetDpi(want);
                    Console.WriteLine("Set to " + want + ", mouse now reports " + d.GetDpi());
                }
            }
            return 0;
        }
    }
}
