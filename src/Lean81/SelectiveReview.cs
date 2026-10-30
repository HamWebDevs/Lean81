using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Lean81
{
    public sealed partial class MainForm
    {
        bool ReviewSession(TuneSessionRequest request, string screenshot = null)
        {
            var proposed = request.Settings;
            DeepTunePlan accepted = null; List<RunningApp> acceptedApps = null;
            bool rememberAccepted = false, forceAccepted = false, measureAccepted = request.Measure;
            using (var dialog = new StyledDialog { Text = "Review deep tune", ClientSize = new Size(880, 650), MinimumSize = new Size(760, 600), StartPosition = FormStartPosition.CenterParent, Font = Font, BackColor = Canvas, Padding = new Padding(18), MinimizeBox = false, MaximizeBox = false })
            {
                var explanation = new Label { Text = "Choose exactly what to change. Uncheck a setting to keep it. Required service providers are kept automatically when you keep a dependent. Nothing changes until you apply.", ForeColor = Ink };
                var tabs = new DesignTabs(); var settingsTab = new TabPage("Choose settings"); var appsTab = new TabPage("Close apps now"); tabs.TabPages.Add(settingsTab); tabs.TabPages.Add(appsTab);
                var settingsBody = new Panel { Dock = DockStyle.Fill }; settingsTab.Controls.Add(settingsBody);
                var settingGrid = Grid(); settingGrid.Name = "TuneSettings";
                settingGrid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Apply", Width = 52, AutoSizeMode = DataGridViewAutoSizeColumnMode.None });
                settingGrid.Columns.Add("Setting", "Setting"); settingGrid.Columns.Add("Type", "Type / scope"); settingGrid.Columns.Add("Effect", "What happens");
                for (int i = 1; i < settingGrid.Columns.Count; i++) settingGrid.Columns[i].ReadOnly = true;
                settingGrid.Columns[1].FillWeight = 39; settingGrid.Columns[2].FillWeight = 23; settingGrid.Columns[3].FillWeight = 38;
                var saved = new HashSet<string>(workspace.KeptTuneItems, StringComparer.Ordinal);
                foreach (var item in proposed.Items) settingGrid.Rows[settingGrid.Rows.Add(!saved.Contains(TuneSelection.Key(item)), item.Name, item.Kind + " / " + item.Scope, "")].Tag = item;
                settingGrid.CurrentCellDirtyStateChanged += (s, e) => { if (settingGrid.IsCurrentCellDirty) settingGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
                var settingDetail = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                    Text = proposed.Items.Count == 0 ? "No settings match your current feature choices. You can still review companion closure." : "Select a row to read its exact target and tradeoff. An unchecked setting is kept. Saved keep choices are already unchecked; you can include them again here." };
                var settingActions = new FlowLayoutPanel { WrapContents = false };
                var keepSettings = ActionButton("Keep all settings", (s, e) => { });
                var useChoices = ActionButton("Use feature choices", (s, e) => { }); settingActions.Controls.Add(keepSettings); settingActions.Controls.Add(useChoices);
                var remember = new CheckBox { Text = "Remember kept settings for the next deep tune", Checked = true, AutoSize = true, Margin = new Padding(0, 8, 0, 0) }; settingActions.Controls.Add(remember);
                Rows(settingsBody, new Control[] { settingGrid, settingDetail, settingActions }, -1, 90, 44);

                var appsBody = new Panel { Dock = DockStyle.Fill }; appsTab.Controls.Add(appsBody);
                var grid = Grid(); grid.Name = "TuneApps";
                grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Close", Width = 52, AutoSizeMode = DataGridViewAutoSizeColumnMode.None });
                grid.Columns.Add("App", "Companion / PID"); grid.Columns.Add("Memory", "RAM MiB"); grid.Columns.Add("Path", "Executable");
                for (int i = 1; i < grid.Columns.Count; i++) grid.Columns[i].ReadOnly = true;
                grid.Columns[1].FillWeight = 30; grid.Columns[2].FillWeight = 15; grid.Columns[3].FillWeight = 55;
                foreach (var app in request.Apps) grid.Rows[grid.Rows.Add(true, app.Name + " / " + app.Pid, (app.Memory / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture), app.Path)].Tag = app;
                grid.CurrentCellDirtyStateChanged += (s, e) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
                var appNote = new Label { Text = request.Apps.Count == 0 ? "No recognized background companions match the unused categories. Browsers, Windows and foreground families are kept." : "Uncheck any companion you need. Closing can interrupt sync, downloads, calls, music or unsaved work. Reopen closed apps manually; RAM working sets can share pages." };
                var appActions = new FlowLayoutPanel();
                var keepAll = ActionButton("Keep all apps", (s, e) => { grid.EndEdit(); foreach (DataGridViewRow row in grid.Rows) row.Cells[0].Value = false; }); appActions.Controls.Add(keepAll);
                Rows(appsBody, new Control[] { appNote, grid, appActions }, 52, -1, 44);
                var options = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false };
                var force = new CheckBox { Text = "Force close selected apps if normal close fails (includes tray / helpers)", AutoSize = true, Margin = new Padding(0, 5, 0, 0) }; options.Controls.Add(force);
                var readings = new CheckBox { Text = "Check CPU, RAM and disk before / after (about 18 seconds; stays inside Lean81)", AutoSize = true, Checked = request.Measure }; options.Controls.Add(readings);
                var summary = new Label { AutoSize = true, ForeColor = Muted, Padding = new Padding(0, 5, 0, 0) }; options.Controls.Add(summary);
                var controls = new FlowLayoutPanel { WrapContents = true };
                bool updating = false; var autoKept = new HashSet<string>(StringComparer.Ordinal);
                Func<DeepTunePlan> selection = () => TuneSelection.Select(proposed, settingGrid.Rows.Cast<DataGridViewRow>().Where(r => Convert.ToBoolean(r.Cells[0].Value)).Select(r => TuneSelection.Key((ScanItem)r.Tag)));
                Action showDetail = () =>
                {
                    if (settingGrid.SelectedRows.Count == 0) return;
                    var row = settingGrid.SelectedRows[0]; var item = (ScanItem)row.Tag;
                    settingDetail.Text = item.Name + " · " + Convert.ToString(row.Cells[3].Value, CultureInfo.InvariantCulture) + "\r\n" + item.Before.Target + "\r\n" + item.Tradeoff
                        + (proposed.Kept.Count == 0 ? "" : "\r\n\r\nOther kept settings:\r\n" + String.Join("\r\n", proposed.Kept));
                };
                var apply = ActionButton("Apply reviewed tune", (s, e) =>
                {
                    settingGrid.EndEdit(); grid.EndEdit();
                    var plan = selection();
                    var chosen = grid.Rows.Cast<DataGridViewRow>().Where(r => Convert.ToBoolean(r.Cells[0].Value)).Select(r => TuneSessionEngine.Copy((RunningApp)r.Tag)).ToList();
                    if (plan.Items.Count + chosen.Count == 0 && !remember.Checked) { summary.Text = "Select a setting or app, remember kept settings, or cancel."; return; }
                    // Validate the complete proposed preference update before accepting.
                    try { if (remember.Checked) TuneSelection.Remember(workspace.KeptTuneItems, proposed, plan); }
                    catch (InvalidOperationException ex) { Error(ex); return; }
                    accepted = plan; acceptedApps = chosen; rememberAccepted = remember.Checked;
                    forceAccepted = force.Checked && chosen.Count != 0; measureAccepted = readings.Checked; dialog.DialogResult = DialogResult.OK;
                }, true);
                var cancel = ActionButton("Cancel", (s, e) => dialog.DialogResult = DialogResult.Cancel); controls.Controls.Add(apply); controls.Controls.Add(cancel);
                Action update = () =>
                {
                    if (updating) return; updating = true;
                    try
                    {
                        var plan = selection(); var allowed = new HashSet<string>(plan.Items.Select(TuneSelection.Key), StringComparer.Ordinal);
                        foreach (DataGridViewRow row in settingGrid.Rows)
                        {
                            var item = (ScanItem)row.Tag; string key = TuneSelection.Key(item);
                            if (Convert.ToBoolean(row.Cells[0].Value) && !allowed.Contains(key)) { row.Cells[0].Value = false; autoKept.Add(key); }
                            if (allowed.Contains(key)) autoKept.Remove(key);
                            row.Cells[3].Value = allowed.Contains(key) ? TuneSelection.Effect(item) : autoKept.Contains(key) ? "Kept: dependent retained" : "Kept by you";
                        }
                        int apps = grid.Rows.Cast<DataGridViewRow>().Count(r => Convert.ToBoolean(r.Cells[0].Value));
                        force.Enabled = apps != 0; if (apps == 0) force.Checked = false;
                        settingsTab.Text = "Settings (" + plan.Items.Count + " / " + proposed.Items.Count + ")";
                        summary.Text = plan.Items.Count + " settings · " + (proposed.Items.Count - plan.Items.Count) + " kept · " + apps + " processes · " + (force.Checked ? "FORCE close may lose work" : "Normal close requests");
                        apply.Text = plan.Items.Count + apps == 0 ? "Save keep list" : plan.Items.Count != 0 && !backend.IsAdministrator ? "Enable changes & rescan" : "Apply reviewed tune";
                        apply.Enabled = plan.Items.Count + apps != 0 || remember.Checked; showDetail();
                    }
                    finally { updating = false; }
                };
                keepSettings.Click += (s, e) => { settingGrid.EndEdit(); updating = true; foreach (DataGridViewRow row in settingGrid.Rows) row.Cells[0].Value = false; autoKept.Clear(); updating = false; update(); };
                useChoices.Click += (s, e) => { settingGrid.EndEdit(); updating = true; foreach (DataGridViewRow row in settingGrid.Rows) row.Cells[0].Value = true; autoKept.Clear(); updating = false; update(); };
                settingGrid.CellValueChanged += (s, e) => update(); settingGrid.SelectionChanged += (s, e) => showDetail();
                grid.CellValueChanged += (s, e) => update(); force.CheckedChanged += (s, e) => update(); remember.CheckedChanged += (s, e) => update(); update();
                var body = new Panel { Dock = DockStyle.Fill }; Rows(body, new Control[] { explanation, tabs, options, controls }, 58, -1, 90, 44); dialog.Controls.Add(body); dialog.CancelButton = cancel;
                dialog.Shown += (s, e) => cancel.Focus();
                using (var timer = new System.Windows.Forms.Timer { Interval = 180 })
                {
                    if (screenshot != null)
                    {
                        timer.Tick += (s, e) =>
                        {
                            timer.Stop(); if (force.Checked || !readings.Checked) throw new InvalidOperationException("Review defaults changed.");
                            using (var image = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(image, new Rectangle(Point.Empty, dialog.Size)); image.Save(Path.Combine(Path.GetDirectoryName(screenshot), "deep-review-settings.png"), System.Drawing.Imaging.ImageFormat.Png); }
                            var provider = settingGrid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => ((ScanItem)r.Tag).Before.Kind == "service" && ((ScanItem)r.Tag).Before.Dependents.Length != 0);
                            if (provider != null)
                            {
                                var target = ((ScanItem)provider.Tag).Before.Dependents[0];
                                var dependent = settingGrid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => ((ScanItem)r.Tag).Before.Target == target);
                                if (dependent != null) { dependent.Cells[0].Value = false; if (Convert.ToBoolean(provider.Cells[0].Value)) throw new InvalidOperationException("Keeping a dependent left its provider selected."); }
                            }
                            keepSettings.PerformClick(); if (selection().Items.Count != 0) throw new InvalidOperationException("Keep all settings failed.");
                            useChoices.PerformClick(); if (selection().Items.Count != proposed.Items.Count) throw new InvalidOperationException("Feature choices were not restored.");
                            tabs.SelectedTab = appsTab; dialog.Refresh();
                            using (var image = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(image, new Rectangle(Point.Empty, dialog.Size)); image.Save(screenshot, System.Drawing.Imaging.ImageFormat.Png); }
                            if (grid.Rows.Count != 0) { force.Checked = true; keepAll.PerformClick(); if (force.Checked || force.Enabled) throw new InvalidOperationException("Keeping apps left force close enabled."); }
                            keepSettings.PerformClick(); if (apply.Text != "Save keep list") throw new InvalidOperationException("The preference-only action is missing.");
                            dialog.DialogResult = DialogResult.Cancel;
                        };
                        dialog.Shown += (s, e) => timer.Start();
                    }
                    if (dialog.ShowDialog(this) != DialogResult.OK) return false;
                }
            }
            if (rememberAccepted)
            {
                workspace.KeptTuneItems = TuneSelection.Remember(workspace.KeptTuneItems, proposed, accepted);
                bool persisted = SaveWorkspace(); status.Text = persisted ? "Keep choices saved · no settings changed yet" : preferencesNotice;
            }
            if (accepted.Items.Count + acceptedApps.Count == 0) { ShowPage(11); return false; }
            request.Settings = accepted; request.Apps = acceptedApps; request.Force = forceAccepted; request.Measure = measureAccepted; return true;
        }
    }
}
