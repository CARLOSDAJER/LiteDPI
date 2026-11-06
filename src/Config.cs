using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace LiteDPI
{
    public class Rule
    {
        public string Match; // "r5apex.exe", a full exe path, or a folder like "C:\Games\Apex\"
        public int Dpi;
        public int? Color;   // null = use the desktop color; ColorText.Off or 0xRRGGBB

        public bool Matches(string exePath, string exeName)
        {
            if (Match.IndexOf('\\') >= 0)
            {
                if (exePath == null) return false;
                if (string.Equals(exePath, Match, StringComparison.OrdinalIgnoreCase)) return true;
                string folder = Match.TrimEnd('\\') + "\\";
                return exePath.StartsWith(folder, StringComparison.OrdinalIgnoreCase);
            }
            string want = Match.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? Match : Match + ".exe";
            return exeName != null && string.Equals(exeName, want, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Lighting values as text: "#RRGGBB", "off", or empty (not set).</summary>
    public static class ColorText
    {
        public const int Off = HidppDevice.ColorOff;

        public static int? Parse(string s)
        {
            if (s == null) return null;
            s = s.Trim();
            if (s.Length == 0) return null;
            if (string.Equals(s, "off", StringComparison.OrdinalIgnoreCase)) return Off;
            s = s.TrimStart('#');
            int v;
            if (s.Length == 6 && int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)) return v;
            return null;
        }

        public static string Format(int? c)
        {
            if (c == null) return "";
            if (c.Value == Off) return "off";
            return "#" + c.Value.ToString("X6");
        }
    }

    /// <summary>LiteDPI.ini next to the exe. Plain text so it can be edited by hand too.</summary>
    public class Config
    {
        public int DesktopDpi = 800;
        public int? DesktopColor;   // null = leave lighting to the mouse
        public List<Rule> Rules = new List<Rule>();

        public static readonly string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LiteDPI.ini");

        public Rule Find(string exePath, string exeName)
        {
            foreach (var r in Rules) if (r.Matches(exePath, exeName)) return r;
            return null;
        }

        /// <summary>The lighting value to send to the mouse for a rule (or the desktop when rule is null).</summary>
        public int EffectiveColor(Rule r)
        {
            int? c = r != null && r.Color != null ? r.Color : DesktopColor;
            return c ?? HidppDevice.ColorDevice;
        }

        public static Config Load()
        {
            var c = new Config();
            if (!File.Exists(FilePath))
            {
                // First run: keep whatever speed the mouse has right now as the desktop speed.
                c.DesktopDpi = CurrentMouseDpi();
                c.Save();
                return c;
            }
            foreach (string raw in File.ReadAllLines(FilePath))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                int eq = line.LastIndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1);
                int comment = value.IndexOf(';'); // allow "cs2.exe = 800, #FF0000  ; comment"
                if (comment >= 0) value = value.Substring(0, comment);

                // value = "<dpi>" or "<dpi>, <color>"
                string[] parts = value.Split(',');
                int dpi;
                if (!int.TryParse(parts[0].Trim(), out dpi) || dpi <= 0) continue;
                int? color = parts.Length > 1 ? ColorText.Parse(parts[1]) : null;

                if (string.Equals(key, "desktop", StringComparison.OrdinalIgnoreCase)) { c.DesktopDpi = dpi; c.DesktopColor = color; }
                else if (key.Length > 0) c.Rules.Add(new Rule { Match = key, Dpi = dpi, Color = color });
            }
            return c;
        }

        static int CurrentMouseDpi()
        {
            try
            {
                using (var d = HidppDevice.Open())
                    if (d != null) { int v = d.GetDpi(); if (v > 0) return v; }
            }
            catch { }
            return 800;
        }

        static string Line(string key, int dpi, int? color)
        {
            return key + " = " + dpi + (color != null ? ", " + ColorText.Format(color) : "");
        }

        public void Save()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# LiteDPI settings. Edit here or from the tray icon > Settings.");
            sb.AppendLine("# desktop = DPI (and optional lighting) used when none of the programs below is in front.");
            sb.AppendLine("# Then one line per game:  <exe name or folder> = <DPI>, <lighting>");
            sb.AppendLine("#   lighting is #RRGGBB or off; leave it out to use the desktop lighting.");
            sb.AppendLine("#   With no desktop lighting, the mouse keeps its own lighting.");
            sb.AppendLine("#   r5apex_dx12.exe = 3000, #FF0000");
            sb.AppendLine("#   C:\\Program Files\\EA Games\\Apex\\ = 3000   (any exe inside that folder)");
            sb.AppendLine();
            sb.AppendLine(Line("desktop", DesktopDpi, DesktopColor));
            foreach (var r in Rules) sb.AppendLine(Line(r.Match, r.Dpi, r.Color));
            File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
        }
    }
}
