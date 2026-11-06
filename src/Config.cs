using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LiteDPI
{
    public class Rule
    {
        public string Match; // "r5apex.exe", a full exe path, or a folder like "C:\Games\Apex\"
        public int Dpi;

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

    /// <summary>LiteDPI.ini next to the exe. Plain text so it can be edited by hand too.</summary>
    public class Config
    {
        public int DesktopDpi = 800;
        public List<Rule> Rules = new List<Rule>();

        public static readonly string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LiteDPI.ini");

        public Rule Find(string exePath, string exeName)
        {
            foreach (var r in Rules) if (r.Matches(exePath, exeName)) return r;
            return null;
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
                int comment = value.IndexOfAny(new[] { ';', '#' }); // allow "cs2.exe = 800  ; comment"
                if (comment >= 0) value = value.Substring(0, comment);
                int dpi;
                if (!int.TryParse(value.Trim(), out dpi) || dpi <= 0) continue;
                if (string.Equals(key, "desktop", StringComparison.OrdinalIgnoreCase)) c.DesktopDpi = dpi;
                else if (key.Length > 0) c.Rules.Add(new Rule { Match = key, Dpi = dpi });
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

        public void Save()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# LiteDPI settings. Edit here or from the tray icon > Settings.");
            sb.AppendLine("# desktop = DPI used when none of the programs below is in front.");
            sb.AppendLine("# Then one line per game:  <exe name or folder> = <DPI>");
            sb.AppendLine("#   r5apex_dx12.exe = 3000");
            sb.AppendLine("#   C:\\Program Files\\EA Games\\Apex\\ = 3000   (any exe inside that folder)");
            sb.AppendLine();
            sb.AppendLine("desktop = " + DesktopDpi);
            foreach (var r in Rules) sb.AppendLine(r.Match + " = " + r.Dpi);
            File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
        }
    }
}
