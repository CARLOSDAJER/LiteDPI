using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LiteDPI
{
    class SettingsForm : Form
    {
        readonly TrayApp app;
        readonly NumericUpDown desktop = new NumericUpDown();
        readonly DataGridView grid = new DataGridView();
        readonly Label status = new Label();
        readonly Timer refresh = new Timer();
        readonly CheckBox startWithWindows = new CheckBox();
        readonly Button deskLight = new Button();
        int? deskColor;

        public SettingsForm(TrayApp app, Config config)
        {
            this.app = app;
            Text = "LiteDPI " + AppInfo.Version + " - Settings";
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(640, 440);
            Size = new Size(660, 520);
            ShowIcon = false;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 7 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // never wider than the window
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            // Desktop DPI
            var top = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0, 0, 0, 8) };
            top.Controls.Add(new Label { Text = "Desktop speed (DPI):", AutoSize = true, Margin = new Padding(0, 6, 8, 0) });
            DpiRange range = app.Worker.Range;
            desktop.Minimum = range != null && range.Step > 0 ? range.Min : 100;
            desktop.Maximum = range != null && range.Step > 0 ? range.Max : 25600;
            desktop.Increment = range != null && range.Step > 0 ? range.Step : 50;
            desktop.Width = 90;
            desktop.Value = Math.Max(desktop.Minimum, Math.Min(desktop.Maximum, config.DesktopDpi));
            top.Controls.Add(desktop);
            top.Controls.Add(new Label { Text = "Lighting:", AutoSize = true, Margin = new Padding(16, 6, 8, 0) });
            deskColor = config.DesktopColor;
            deskLight.AutoSize = false;
            deskLight.Size = new Size(120, desktop.Height + 2);
            deskLight.FlatStyle = FlatStyle.Flat;
            deskLight.Margin = new Padding(0, 1, 0, 0);
            deskLight.Click += delegate
            {
                ShowLightingMenu(deskLight, new Point(0, deskLight.Height), "Mouse's own lighting", deskColor, delegate(int? c)
                {
                    deskColor = c;
                    PaintDeskLight();
                    grid.Invalidate();
                    app.Try((int)desktop.Value, Effective(null));
                });
            };
            PaintDeskLight();
            top.Controls.Add(deskLight);
            var tryDesktop = new Button { Text = "Try it", AutoSize = true, Margin = new Padding(8, 1, 0, 0) };
            tryDesktop.Click += delegate { app.Try((int)desktop.Value, Effective(null)); };
            top.Controls.Add(tryDesktop);
            root.Controls.Add(top);

            root.Controls.Add(new Label
            {
                Text = "Games: while one of these is the window in front, the mouse uses its DPI and lighting. " +
                       "Use an exe name (r5apex_dx12.exe) or a folder (C:\\Games\\Apex\\). Click a Lighting cell to pick a color.",
                AutoSize = true, MaximumSize = new Size(610, 0), Margin = new Padding(0, 0, 0, 6)
            });

            // Rules grid
            grid.Dock = DockStyle.Fill;
            grid.AllowUserToAddRows = true;
            grid.AllowUserToDeleteRows = true;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.BackgroundColor = SystemColors.Window;
            grid.BorderStyle = BorderStyle.FixedSingle;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Program", HeaderText = "Game (exe name or folder)", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Dpi", HeaderText = "DPI", Width = 80 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Light", HeaderText = "Lighting", Width = 120, ReadOnly = true });
            foreach (var r in config.Rules) grid.Rows.Add(r.Match, r.Dpi, ColorText.Format(r.Color));
            grid.CellFormatting += FormatLightCell;
            grid.CellClick += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0 || e.ColumnIndex != 2 || grid.Rows[e.RowIndex].IsNewRow) return;
                var row = grid.Rows[e.RowIndex];
                Rectangle cell = grid.GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, false);
                ShowLightingMenu(grid, new Point(cell.Left, cell.Bottom), "Same as desktop", ColorText.Parse(Convert.ToString(row.Cells[2].Value)), delegate(int? c)
                {
                    row.Cells[2].Value = ColorText.Format(c);
                    int dpi;
                    if (int.TryParse(Convert.ToString(row.Cells[1].Value), out dpi) && dpi > 0) app.Try(dpi, Effective(c));
                });
            };
            root.Controls.Add(grid);

            // Row buttons
            var rowButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 0) };
            var add = new Button { Text = "Add a running program...", AutoSize = true };
            add.Click += delegate { ShowRunningPrograms(add); };
            var remove = new Button { Text = "Remove", AutoSize = true };
            remove.Click += delegate
            {
                foreach (DataGridViewRow row in grid.SelectedRows) if (!row.IsNewRow) grid.Rows.Remove(row);
            };
            var tryRow = new Button { Text = "Try selected game", AutoSize = true };
            tryRow.Click += delegate
            {
                if (grid.CurrentRow == null || grid.CurrentRow.IsNewRow) return;
                int dpi;
                if (int.TryParse(Convert.ToString(grid.CurrentRow.Cells[1].Value), out dpi) && dpi > 0)
                    app.Try(dpi, Effective(ColorText.Parse(Convert.ToString(grid.CurrentRow.Cells[2].Value))));
            };
            rowButtons.Controls.Add(add);
            rowButtons.Controls.Add(remove);
            rowButtons.Controls.Add(tryRow);
            root.Controls.Add(rowButtons);

            // Start with Windows (applied on Save)
            startWithWindows.Text = "Start LiteDPI when Windows starts";
            startWithWindows.AutoSize = true;
            startWithWindows.Checked = Startup.IsEnabled();
            startWithWindows.Margin = new Padding(0, 10, 0, 0);
            root.Controls.Add(startWithWindows);

            // Mouse status line
            status.AutoSize = false;
            status.AutoEllipsis = true;
            status.Dock = DockStyle.Fill;
            status.Height = status.Font.Height + 8;
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.ForeColor = SystemColors.GrayText;
            status.Margin = new Padding(0, 8, 0, 0);
            root.Controls.Add(status);

            // Save / Cancel, right-aligned
            var bottom = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 4, 0, 0) };
            var save = new Button { Text = "Save", AutoSize = true, MinimumSize = new Size(80, 0) };
            var cancel = new Button { Text = "Cancel", AutoSize = true, MinimumSize = new Size(80, 0), DialogResult = DialogResult.Cancel };
            save.Click += delegate { Save(); };
            cancel.Click += delegate { Close(); };
            AcceptButton = save;
            CancelButton = cancel;
            bottom.Controls.Add(cancel);
            bottom.Controls.Add(save);
            root.Controls.Add(bottom);

            refresh.Interval = 1000;
            refresh.Tick += delegate { UpdateStatus(); };
            refresh.Start();
            UpdateStatus();
            FormClosed += delegate { refresh.Dispose(); };
        }

        void UpdateStatus()
        {
            var w = app.Worker;
            if (w.DeviceName == null) { status.Text = "Mouse not found"; return; }
            status.Text = w.DeviceName + " is at " + w.AppliedDpi + " DPI" + (w.Range != null ? " (supports " + w.Range + ")" : "");
        }

        void ShowRunningPrograms(Control anchor)
        {
            var menu = new ContextMenuStrip();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var items = new List<KeyValuePair<string, string>>();
            int me = Process.GetCurrentProcess().Id;
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id != me && p.MainWindowHandle != IntPtr.Zero && p.MainWindowTitle.Length > 0)
                    {
                        string exe = p.ProcessName + ".exe";
                        if (seen.Add(exe)) items.Add(new KeyValuePair<string, string>(exe, p.MainWindowTitle));
                    }
                }
                catch { }
                finally { p.Dispose(); }
            }
            foreach (var it in items.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                string exe = it.Key;
                string title = it.Value.Length > 50 ? it.Value.Substring(0, 50) + "..." : it.Value;
                menu.Items.Add(exe + "   (" + title + ")", null, delegate
                {
                    int i = grid.Rows.Add(exe, (int)desktop.Value, "");
                    grid.CurrentCell = grid.Rows[i].Cells[1];
                    grid.BeginEdit(true);
                });
            }
            if (menu.Items.Count == 0) menu.Items.Add("(no programs with windows found)").Enabled = false;
            menu.Show(anchor, new Point(0, anchor.Height));
        }

        void Save()
        {
            grid.EndEdit();
            DpiRange range = app.Worker.Range;
            var c = new Config { DesktopDpi = Snap((int)desktop.Value, range), DesktopColor = deskColor };
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.IsNewRow) continue;
                string prog = Convert.ToString(row.Cells[0].Value).Trim();
                string dpiText = Convert.ToString(row.Cells[1].Value).Trim();
                if (prog.Length == 0 && dpiText.Length == 0) continue;
                int dpi;
                if (prog.Length == 0 || !int.TryParse(dpiText, out dpi) || dpi < 50 || dpi > 50000)
                {
                    grid.CurrentCell = row.Cells[prog.Length == 0 ? 0 : 1];
                    MessageBox.Show(this, "Each game needs a program name and a DPI number (for example 1600).", "LiteDPI");
                    return;
                }
                c.Rules.Add(new Rule { Match = prog, Dpi = Snap(dpi, range), Color = ColorText.Parse(Convert.ToString(row.Cells[2].Value)) });
            }
            try
            {
                c.Save();
                if (startWithWindows.Checked != Startup.IsEnabled()) Startup.Set(startWithWindows.Checked);
            }
            catch (Exception e) { MessageBox.Show(this, "Couldn't save settings: " + e.Message, "LiteDPI"); return; }
            app.ReloadConfig(c);
            Close();
        }

        static int Snap(int dpi, DpiRange range) { return range != null ? range.Snap(dpi) : dpi; }

        // ---------- lighting ----------

        static readonly KeyValuePair<string, int>[] Presets =
        {
            new KeyValuePair<string, int>("Red", 0xFF0000),
            new KeyValuePair<string, int>("Orange", 0xFF6A00),
            new KeyValuePair<string, int>("Yellow", 0xFFD000),
            new KeyValuePair<string, int>("Green", 0x00FF40),
            new KeyValuePair<string, int>("Cyan", 0x00E5FF),
            new KeyValuePair<string, int>("Blue", 0x0040FF),
            new KeyValuePair<string, int>("Purple", 0x9000FF),
            new KeyValuePair<string, int>("Pink", 0xFF00A0),
            new KeyValuePair<string, int>("White", 0xFFFFFF),
        };

        /// <summary>What the mouse should show for a game color (null = the desktop's).</summary>
        int Effective(int? gameColor)
        {
            int? c = gameColor ?? deskColor;
            return c ?? HidppDevice.ColorDevice;
        }

        static Color ToColor(int rgb) { return Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF); }

        static Color TextOn(Color bg)
        {
            return bg.R * 0.299 + bg.G * 0.587 + bg.B * 0.114 > 150 ? Color.Black : Color.White;
        }

        static Bitmap Swatch(Color c)
        {
            var b = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(b))
            {
                g.Clear(c);
                g.DrawRectangle(Pens.Gray, 0, 0, 15, 15);
            }
            return b;
        }

        void ShowLightingMenu(Control owner, Point at, string noneLabel, int? current, Action<int?> picked)
        {
            var menu = new ContextMenuStrip();
            var none = menu.Items.Add(noneLabel, null, delegate { picked(null); });
            if (current == null) none.Font = new Font(menu.Font, FontStyle.Bold);
            var off = menu.Items.Add("Off", null, delegate { picked(ColorText.Off); });
            if (current != null && current.Value == ColorText.Off) off.Font = new Font(menu.Font, FontStyle.Bold);
            menu.Items.Add(new ToolStripSeparator());
            foreach (var p in Presets)
            {
                int rgb = p.Value;
                var item = menu.Items.Add(p.Key, Swatch(ToColor(rgb)), delegate { picked(rgb); });
                if (current != null && current.Value == rgb) item.Font = new Font(menu.Font, FontStyle.Bold);
            }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Other color...", null, delegate
            {
                using (var dlg = new ColorDialog { FullOpen = true, AnyColor = true })
                {
                    if (current != null && current.Value >= 0) dlg.Color = ToColor(current.Value);
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                        picked((dlg.Color.R << 16) | (dlg.Color.G << 8) | dlg.Color.B);
                }
            });
            menu.Show(owner, at);
        }

        void PaintDeskLight()
        {
            if (deskColor == null || deskColor.Value == ColorText.Off)
            {
                deskLight.BackColor = SystemColors.Control;
                deskLight.ForeColor = SystemColors.ControlText;
                deskLight.Text = deskColor == null ? "Mouse's own" : "Off";
            }
            else
            {
                Color c = ToColor(deskColor.Value);
                deskLight.BackColor = c;
                deskLight.ForeColor = TextOn(c);
                deskLight.Text = ColorText.Format(deskColor);
            }
        }

        void FormatLightCell(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.ColumnIndex != 2 || e.RowIndex < 0 || grid.Rows[e.RowIndex].IsNewRow) return;
            int? c = ColorText.Parse(Convert.ToString(e.Value));
            if (c == null)
            {
                e.Value = "Desktop";
                e.CellStyle.ForeColor = SystemColors.GrayText;
            }
            else if (c.Value == ColorText.Off)
            {
                e.Value = "Off";
            }
            else
            {
                Color col = ToColor(c.Value);
                e.Value = ColorText.Format(c);
                e.CellStyle.BackColor = col;
                e.CellStyle.SelectionBackColor = col;
                e.CellStyle.ForeColor = TextOn(col);
                e.CellStyle.SelectionForeColor = TextOn(col);
            }
            e.FormattingApplied = true;
        }
    }
}
