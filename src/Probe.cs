// Console diagnostic: litedpi-probe            -> show mouse info
//                     litedpi-probe set 1600   -> set DPI
using System;

namespace LiteDPI
{
    static class Probe
    {
        static string Hex(byte[] b)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var x in b) sb.Append(x.ToString("X2")).Append(' ');
            return sb.ToString().TrimEnd();
        }

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

                if (args.Length >= 1 && args[0] == "features")
                {
                    foreach (int f in d.ListFeatures())
                        Console.WriteLine("  feature 0x" + f.ToString("X4") + " at index " + d.FeatureIndex(f));
                    foreach (int lf in new[] { 0x8070, 0x8071 })
                    {
                        byte fi = d.FeatureIndex(lf);
                        if (fi == 0) continue;
                        Console.WriteLine("Lighting feature 0x" + lf.ToString("X4"));
                        if (lf == 0x8070)
                        {
                            var info = d.Request(fi, 0);
                            Console.WriteLine("  info: " + Hex(info));
                            for (int z = 0; z < info[0]; z++)
                            {
                                var zi = d.Request(fi, 1, (byte)z);
                                Console.WriteLine("  zone " + z + ": " + Hex(zi));
                                for (int e = 0; e < zi[3]; e++)
                                    Console.WriteLine("    effect " + e + ": " + Hex(d.Request(fi, 2, (byte)z, (byte)e)));
                            }
                            Console.WriteLine("  sw control: " + Hex(d.Request(fi, 7)));
                        }
                        else Console.WriteLine("  info: " + Hex(d.Request(fi, 0, 0xFF, 0xFF, 0x00)));
                    }
                }

                if (args.Length == 2 && args[0] == "color")
                {
                    // litedpi-probe color FF0000 | off | device
                    int c = args[1] == "off" ? HidppDevice.ColorOff
                          : args[1] == "device" ? HidppDevice.ColorDevice
                          : Convert.ToInt32(args[1].TrimStart('#'), 16);
                    d.SetLighting(c);
                    Console.WriteLine("Lighting set. Zone 0: " + Hex(d.GetZoneEffect(0)) + " | Zone 1: " + Hex(d.GetZoneEffect(1)));
                }

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
