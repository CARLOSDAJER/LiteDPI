// Talks to Logitech mice over HID++ 2.0 using plain Windows HID APIs.
// No drivers, no G HUB. Protocol reference: Solaar / libratbag (open source).
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace LiteDPI
{
    internal static class Native
    {
        public const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;
        public const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        public const int DIGCF_PRESENT = 0x2, DIGCF_DEVICEINTERFACE = 0x10;
        public const int ERROR_IO_PENDING = 997;
        public const int HIDP_STATUS_SUCCESS = 0x00110000;
        public static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
                NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
                NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct OVERLAPPED { public IntPtr Internal, InternalHigh; public int Offset, OffsetHigh; public IntPtr hEvent; }

        [DllImport("hid.dll")] public static extern void HidD_GetHidGuid(out Guid g);
        [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
        [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr pp);
        [DllImport("hid.dll")] public static extern bool HidD_FreePreparsedData(IntPtr pp);
        [DllImport("hid.dll")] public static extern int HidP_GetCaps(IntPtr pp, out HIDP_CAPS caps);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr enumerator, IntPtr hwnd, int flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid g, int index, ref SP_DEVICE_INTERFACE_DATA did);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA did, IntPtr detail, int size, out int required, IntPtr devInfo);
        [DllImport("setupapi.dll")] public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool ReadFile(SafeFileHandle h, IntPtr buf, int len, IntPtr read, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool WriteFile(SafeFileHandle h, IntPtr buf, int len, IntPtr written, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GetOverlappedResult(SafeFileHandle h, IntPtr ov, out int bytes, bool wait);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool CancelIoEx(SafeFileHandle h, IntPtr ov);
    }

    public class HidInfo
    {
        public string Path;
        public ushort Vid, Pid, UsagePage, Usage;
        public int InLen, OutLen;
        public override string ToString()
        {
            return string.Format("{0:X4}:{1:X4} page {2:X4} usage {3:X4} in {4} out {5}", Vid, Pid, UsagePage, Usage, InLen, OutLen);
        }
    }

    public static class Hid
    {
        public static List<HidInfo> Enumerate(ushort vid)
        {
            var list = new List<HidInfo>();
            Guid g;
            Native.HidD_GetHidGuid(out g);
            IntPtr set = Native.SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, Native.DIGCF_PRESENT | Native.DIGCF_DEVICEINTERFACE);
            if (set == Native.INVALID_HANDLE_VALUE) return list;
            try
            {
                var did = new Native.SP_DEVICE_INTERFACE_DATA();
                did.cbSize = Marshal.SizeOf(did);
                for (int i = 0; Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref did); i++)
                {
                    int req;
                    Native.SetupDiGetDeviceInterfaceDetail(set, ref did, IntPtr.Zero, 0, out req, IntPtr.Zero);
                    if (req <= 0) continue;
                    IntPtr det = Marshal.AllocHGlobal(req);
                    try
                    {
                        Marshal.WriteInt32(det, IntPtr.Size == 8 ? 8 : 6);
                        if (!Native.SetupDiGetDeviceInterfaceDetail(set, ref did, det, req, out req, IntPtr.Zero)) continue;
                        string path = Marshal.PtrToStringUni(IntPtr.Add(det, 4));
                        HidInfo info = Query(path);
                        if (info != null && info.Vid == vid) list.Add(info);
                    }
                    finally { Marshal.FreeHGlobal(det); }
                }
            }
            finally { Native.SetupDiDestroyDeviceInfoList(set); }
            return list;
        }

        static HidInfo Query(string path)
        {
            using (var h = Native.CreateFile(path, 0, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero))
            {
                if (h.IsInvalid) return null;
                var a = new Native.HIDD_ATTRIBUTES();
                a.Size = Marshal.SizeOf(a);
                if (!Native.HidD_GetAttributes(h, ref a)) return null;
                var info = new HidInfo { Path = path, Vid = a.VendorID, Pid = a.ProductID };
                IntPtr pp;
                if (Native.HidD_GetPreparsedData(h, out pp))
                {
                    try
                    {
                        Native.HIDP_CAPS c;
                        if (Native.HidP_GetCaps(pp, out c) == Native.HIDP_STATUS_SUCCESS)
                        {
                            info.UsagePage = c.UsagePage; info.Usage = c.Usage;
                            info.InLen = c.InputReportByteLength; info.OutLen = c.OutputReportByteLength;
                        }
                    }
                    finally { Native.HidD_FreePreparsedData(pp); }
                }
                return info;
            }
        }
    }

    /// <summary>One opened HID collection with overlapped (timeout-capable) reads and writes.</summary>
    public sealed class HidHandle : IDisposable
    {
        static readonly int OvSize = Marshal.SizeOf(typeof(Native.OVERLAPPED));
        readonly SafeFileHandle h;
        public readonly int InLen, OutLen;
        readonly ManualResetEvent rEv = new ManualResetEvent(false), wEv = new ManualResetEvent(false);
        // Buffers and OVERLAPPED live in unmanaged memory so the GC can't move them mid-I/O.
        readonly IntPtr rBuf, wBuf, rOv, wOv;
        bool readPending, disposed;

        public HidHandle(HidInfo info)
        {
            h = Native.CreateFile(info.Path, Native.GENERIC_READ | Native.GENERIC_WRITE,
                Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, Native.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (h.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            InLen = info.InLen; OutLen = info.OutLen;
            rBuf = Marshal.AllocHGlobal(Math.Max(InLen, 1));
            wBuf = Marshal.AllocHGlobal(Math.Max(OutLen, 1));
            rOv = Marshal.AllocHGlobal(OvSize);
            wOv = Marshal.AllocHGlobal(OvSize);
        }

        static void PrepOv(IntPtr ov, ManualResetEvent ev)
        {
            ev.Reset();
            var o = new Native.OVERLAPPED();
            o.hEvent = ev.SafeWaitHandle.DangerousGetHandle();
            Marshal.StructureToPtr(o, ov, false);
        }

        public void Write(byte[] data)
        {
            var buf = new byte[OutLen];
            Array.Copy(data, buf, Math.Min(data.Length, OutLen));
            Marshal.Copy(buf, 0, wBuf, OutLen);
            PrepOv(wOv, wEv);
            if (!Native.WriteFile(h, wBuf, OutLen, IntPtr.Zero, wOv))
            {
                int err = Marshal.GetLastWin32Error();
                if (err != Native.ERROR_IO_PENDING) throw new Win32Exception(err);
            }
            int n;
            if (!wEv.WaitOne(1000))
            {
                Native.CancelIoEx(h, wOv);
                Native.GetOverlappedResult(h, wOv, out n, true);
                throw new TimeoutException("HID write timed out");
            }
            if (!Native.GetOverlappedResult(h, wOv, out n, false)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        /// <summary>Returns the next input report, or null on timeout (the read stays queued for next time).</summary>
        public byte[] Read(int timeoutMs)
        {
            if (!readPending)
            {
                PrepOv(rOv, rEv);
                if (!Native.ReadFile(h, rBuf, InLen, IntPtr.Zero, rOv))
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err != Native.ERROR_IO_PENDING) throw new Win32Exception(err);
                }
                readPending = true;
            }
            if (!rEv.WaitOne(Math.Max(0, timeoutMs))) return null;
            readPending = false;
            int n;
            if (!Native.GetOverlappedResult(h, rOv, out n, false)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var r = new byte[n];
            Marshal.Copy(rBuf, r, 0, n);
            return r;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (readPending)
            {
                int n;
                Native.CancelIoEx(h, rOv);
                Native.GetOverlappedResult(h, rOv, out n, true); // wait so the kernel is done with our buffer
                readPending = false;
            }
            h.Dispose();
            Marshal.FreeHGlobal(rBuf); Marshal.FreeHGlobal(wBuf);
            Marshal.FreeHGlobal(rOv); Marshal.FreeHGlobal(wOv);
            rEv.Close(); wEv.Close();
        }
    }

    public class HidppException : Exception
    {
        public readonly int Code;
        public HidppException(int code, string msg) : base(msg) { Code = code; }
    }

    public class DpiRange
    {
        public int Min, Max, Step;          // Step > 0: continuous range
        public List<int> Values = new List<int>(); // Step == 0: fixed list

        public int Snap(int dpi)
        {
            if (Step > 0)
            {
                dpi = Math.Max(Min, Math.Min(Max, dpi));
                return Min + (int)Math.Round((dpi - Min) / (double)Step) * Step;
            }
            int best = dpi, bestDiff = int.MaxValue;
            foreach (int v in Values) { int d = Math.Abs(v - dpi); if (d < bestDiff) { best = v; bestDiff = d; } }
            return best;
        }

        public override string ToString()
        {
            if (Step > 0) return string.Format("{0}-{1} DPI in steps of {2}", Min, Max, Step);
            return string.Join(", ", Values) + " DPI";
        }
    }

    /// <summary>A Logitech HID++ 2.0 mouse.</summary>
    public sealed class HidppDevice : IDisposable
    {
        const byte LONG_REPORT = 0x11, SHORT_REPORT = 0x10;
        const int FEAT_ROOT = 0x0000, FEAT_NAME = 0x0005, FEAT_DPI = 0x2201, FEAT_ONBOARD = 0x8100;

        readonly HidHandle io;
        readonly byte dev;
        readonly Dictionary<int, byte> feats = new Dictionary<int, byte>();
        public readonly HidInfo Info;
        public string Name { get; private set; }
        public byte DeviceIndex { get { return dev; } }

        HidppDevice(HidInfo info, HidHandle io, byte dev) { Info = info; this.io = io; this.dev = dev; Name = "Logitech mouse"; }

        public static string LastError = "";

        /// <summary>Opens the first Logitech device that answers HID++ 2.0 and has adjustable DPI. Null if none.</summary>
        public static HidppDevice Open()
        {
            LastError = "no Logitech HID++ channel found";
            foreach (var info in Hid.Enumerate(0x046D))
            {
                // HID++ long reports: vendor usage page (0xFF00 or 0xFF43), 20-byte output report.
                if ((info.UsagePage & 0xFF00) != 0xFF00 || info.OutLen != 20) continue;
                HidHandle io;
                try { io = new HidHandle(info); }
                catch (Exception e) { LastError = "open failed: " + e.Message; continue; }
                foreach (byte d in new byte[] { 0xFF, 0x01 }) // 0xFF = wired, 0x01 = first device on a receiver
                {
                    var dev = new HidppDevice(info, io, d);
                    try
                    {
                        if (dev.Ping() && dev.FeatureIndex(FEAT_DPI) != 0)
                        {
                            dev.Name = dev.ReadName();
                            return dev;
                        }
                        LastError = "device 0x" + d.ToString("X2") + " has no adjustable DPI";
                    }
                    catch (Exception e) { LastError = "device 0x" + d.ToString("X2") + ": " + e.GetType().Name + ": " + e.Message; }
                }
                io.Dispose();
            }
            return null;
        }

        // The G502 sometimes holds a reply back until the next request arrives, then sends the
        // previous reply first. So every request gets its own software ID (1-15) and only a reply
        // carrying that ID counts; if nothing matches quickly, the same request is sent again.
        static int swCounter; // shared by every device object in this process

        public byte[] Request(byte feature, int function, params byte[] args)
        {
            int sw = (int)((uint)Interlocked.Increment(ref swCounter) % 15) + 1;
            var msg = new byte[20];
            msg[0] = LONG_REPORT; msg[1] = dev; msg[2] = feature; msg[3] = (byte)((function << 4) | sw);
            if (args != null) Array.Copy(args, 0, msg, 4, Math.Min(args.Length, 16));

            while (io.Read(0) != null) { } // drop anything already buffered

            const int attempts = 4;
            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                io.Write(msg);
                int deadline = Environment.TickCount + (attempt < attempts ? 100 : 500);
                while (true)
                {
                    int left = deadline - Environment.TickCount;
                    byte[] r = left > 0 ? io.Read(left) : null;
                    if (r == null) break; // no match yet: send again
                    if (r.Length < 7 || r[1] != dev) continue; // other traffic (G HUB, notifications)
                    if (r[0] == LONG_REPORT && r[2] == feature && r[3] == msg[3])
                    {
                        var p = new byte[16];
                        Array.Copy(r, 4, p, 0, Math.Min(16, r.Length - 4));
                        return p;
                    }
                    if (r[2] == 0xFF && r[3] == feature && r[4] == msg[3])
                        throw new HidppException(r[5], "Mouse rejected the request (HID++ error " + r[5] + ")");
                    if (r[0] == SHORT_REPORT && r[2] == 0x8F && r[3] == feature && r[4] == msg[3])
                        throw new HidppException(r[5], "Mouse rejected the request (HID++ 1.0 error " + r[5] + ")");
                }
            }
            throw new TimeoutException("The mouse did not answer");
        }

        public bool Ping()
        {
            var r = Request(0, 1, 0, 0, 0x5A); // root.getProtocolVersion echoes the last byte
            return r[2] == 0x5A;
        }

        public byte FeatureIndex(int featureId)
        {
            byte idx;
            if (featureId == FEAT_ROOT) return 0;
            if (feats.TryGetValue(featureId, out idx)) return idx;
            idx = Request(0, 0, (byte)(featureId >> 8), (byte)featureId)[0];
            feats[featureId] = idx;
            return idx;
        }

        string ReadName()
        {
            try
            {
                byte fi = FeatureIndex(FEAT_NAME);
                if (fi == 0) return Name;
                int len = Request(fi, 0)[0];
                var sb = new StringBuilder();
                while (sb.Length < len)
                {
                    var r = Request(fi, 1, (byte)sb.Length);
                    for (int i = 0; i < 16 && sb.Length < len; i++)
                    {
                        if (r[i] == 0) { len = sb.Length; break; }
                        sb.Append((char)r[i]);
                    }
                }
                return sb.Length > 0 ? sb.ToString() : Name;
            }
            catch { return Name; }
        }

        byte DpiFeature()
        {
            byte fi = FeatureIndex(FEAT_DPI);
            if (fi == 0) throw new NotSupportedException("This mouse has no adjustable DPI");
            return fi;
        }

        public int GetDpi()
        {
            var r = Request(DpiFeature(), 2, 0);
            return (r[1] << 8) | r[2];
        }

        public void SetDpi(int dpi)
        {
            Request(DpiFeature(), 3, 0, (byte)(dpi >> 8), (byte)dpi);
        }

        public DpiRange GetDpiRange()
        {
            var r = Request(DpiFeature(), 1, 0);
            var range = new DpiRange();
            for (int i = 1; i + 1 < 16; i += 2)
            {
                int w = (r[i] << 8) | r[i + 1];
                if (w == 0) break;
                if ((w >> 13) == 7) range.Step = w & 0x1FFF; // 0xE000 | step marks a range
                else range.Values.Add(w);
            }
            if (range.Step > 0 && range.Values.Count >= 2)
            {
                range.Min = range.Values[0];
                range.Max = range.Values[range.Values.Count - 1];
            }
            else range.Step = 0;
            return range;
        }

        /// <summary>1 = onboard memory, 2 = host (software) mode, 0 = no onboard profiles.</summary>
        public int GetOnboardMode()
        {
            byte fi = FeatureIndex(FEAT_ONBOARD);
            if (fi == 0) return 0;
            return Request(fi, 2)[0];
        }

        /// <summary>Switches between onboard memory (1) and host mode (2). Not saved to flash; resets on replug.</summary>
        public void SetOnboardMode(int mode)
        {
            byte fi = FeatureIndex(FEAT_ONBOARD);
            if (fi == 0) return;
            Request(fi, 1, (byte)mode);
        }

        public void Dispose() { io.Dispose(); }
    }
}
