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

        public SettingsForm(TrayApp app, Config config)
        {
            this.app = app;
            Text = "LiteDPI " + AppInfo.Version + " - Settings";
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(600, 440);
            Size = new Size(600, 500);
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
            var tryDesktop = new Button { Text = "Try it", AutoSize = true, Margin = new Padding(8, 2, 0, 0) };
            tryDesktop.Click += delegate { app.TryDpi((int)desktop.Value); };
            top.Controls.Add(tryDesktop);
            root.Controls.Add(top);

            root.Controls.Add(new Label
            {
                Text = "Games: while one of these is the window in front, the mouse uses its DPI. " +
                       "Use an exe name (r5apex_dx12.exe) or a folder (C:\\Games\\Apex\\).",
                AutoSize = true, MaximumSize = new Size(560, 0), Margin = new Padding(0, 0, 0, 6)
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
            foreach (var r in config.Rules) grid.Rows.Add(r.Match, r.Dpi);
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
            var tryRow = new Button { Text = "Try selected DPI", AutoSize = true };
            tryRow.Click += delegate
            {
                if (grid.CurrentRow == null) return;
                int dpi;
                if (int.TryParse(Convert.ToString(grid.CurrentRow.Cells[1].Value), out dpi) && dpi > 0) app.TryDpi(dpi);
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
                    int i = grid.Rows.Add(exe, (int)desktop.Value);
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
            var c = new Config { DesktopDpi = Snap((int)desktop.Value, range) };
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
                c.Rules.Add(new Rule { Match = prog, Dpi = Snap(dpi, range) });
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
    }
}
