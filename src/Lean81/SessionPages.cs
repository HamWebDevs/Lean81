using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Lean81
{
    public sealed partial class MainForm
    {
        TuneSessionResult lastSession;
        bool ReviewSession(TuneSessionRequest request, string screenshot = null)
        {
            using (var dialog = new Form { Text = "Review deep tune", ClientSize = new Size(800, 570), MinimumSize = new Size(720, 530), StartPosition = FormStartPosition.CenterParent, Font = Font, BackColor = Canvas, Padding = new Padding(18), MinimizeBox = false, MaximizeBox = false })
            {
                var explanation = new Label { Text = "Review the settings and choose which running companions to close. Services stop now and stay disabled. Startup changes affect future launches. Closed apps must be reopened manually.", ForeColor = Ink };
                var tabs = new TabControl(); var settingsTab = new TabPage("Settings (" + request.Settings.Items.Count + ")"); var appsTab = new TabPage("Close apps now"); tabs.TabPages.Add(settingsTab); tabs.TabPages.Add(appsTab);
                settingsTab.Controls.Add(new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, BackColor = Color.White, ScrollBars = ScrollBars.Vertical,
                    Text = request.Settings.Items.Count == 0 ? "No settings to change in this review. You can still close reviewed background apps." : String.Join(Environment.NewLine + Environment.NewLine, request.Settings.Items.Select(i => i.Name + " · " + i.Scope + " / " + i.Kind + "\r\n" + i.Before.Target + "\r\n" + i.Tradeoff).Concat(request.Settings.Kept.Select(k => "KEPT: " + k))) });
                var appsBody = new Panel { Dock = DockStyle.Fill }; appsTab.Controls.Add(appsBody);
                var grid = Grid(); grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Close", Width = 52, AutoSizeMode = DataGridViewAutoSizeColumnMode.None });
                grid.Columns.Add("App", "Companion / PID"); grid.Columns.Add("Memory", "RAM MiB"); grid.Columns.Add("Path", "Executable");
                for (int i = 1; i < grid.Columns.Count; i++) grid.Columns[i].ReadOnly = true;
                grid.Columns[1].FillWeight = 30; grid.Columns[2].FillWeight = 15; grid.Columns[3].FillWeight = 55;
                foreach (var app in request.Apps) grid.Rows[grid.Rows.Add(true, app.Name + " / " + app.Pid, (app.Memory / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture), app.Path)].Tag = app;
                grid.CurrentCellDirtyStateChanged += (s, e) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
                var appNote = new Label { Text = request.Apps.Count == 0 ? "No recognized background companions match the unused categories. Browsers, Windows and foreground families are kept." : "Uncheck any app you need. Closing can interrupt sync, downloads, calls, music or unsaved app state. RAM working sets can include shared pages." };
                var appActions = new FlowLayoutPanel();
                appActions.Controls.Add(ActionButton("Keep all apps", (s, e) => { grid.EndEdit(); foreach (DataGridViewRow row in grid.Rows) row.Cells[0].Value = false; }));
                var force = new CheckBox { Text = "Force close selected tray / helper processes if normal close fails", AutoSize = true, Margin = new Padding(0, 9, 0, 0) };
                Rows(appsBody, new Control[] { appNote, grid, appActions }, 52, -1, 45);
                var options = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false };
                options.Controls.Add(force);
                var readings = new CheckBox { Text = "Check CPU, RAM and disk before / after (about 18 seconds; stays inside Lean81)", AutoSize = true, Checked = request.Measure };
                options.Controls.Add(readings);
                var summary = new Label { AutoSize = true, ForeColor = Muted, Padding = new Padding(0, 5, 0, 0) }; options.Controls.Add(summary);
                var controls = new FlowLayoutPanel { WrapContents = true };
                var apply = ActionButton(request.Settings.Items.Count != 0 && !backend.IsAdministrator ? "Enable changes & rescan" : "Apply reviewed tune", (s, e) =>
                {
                    grid.EndEdit();
                    var chosen = grid.Rows.Cast<DataGridViewRow>().Where(r => Convert.ToBoolean(r.Cells[0].Value)).Select(r => TuneSessionEngine.Copy((RunningApp)r.Tag)).ToList();
                    if (request.Settings.Items.Count + chosen.Count == 0) { summary.Text = "Choose at least one app or cancel this review."; return; }
                    request.Apps = chosen; request.Force = force.Checked && chosen.Count != 0; request.Measure = readings.Checked;
                    dialog.DialogResult = DialogResult.OK;
                }, true);
                var cancel = ActionButton("Cancel", (s, e) => dialog.DialogResult = DialogResult.Cancel); controls.Controls.Add(apply); controls.Controls.Add(cancel);
                Action update = () => { int count = grid.Rows.Cast<DataGridViewRow>().Count(r => Convert.ToBoolean(r.Cells[0].Value)); force.Enabled = count != 0; if (count == 0) force.Checked = false; summary.Text = request.Settings.Items.Count + " settings · " + count + " processes · " + (force.Checked ? "FORCE close may lose app work" : "Normal close requests only"); };
                grid.CellValueChanged += (s, e) => update(); force.CheckedChanged += (s, e) => update(); update();
                var body = new Panel { Dock = DockStyle.Fill }; Rows(body, new Control[] { explanation, tabs, options, controls }, 65, -1, 95, 45); dialog.Controls.Add(body); dialog.CancelButton = cancel;
                dialog.Shown += (s, e) => cancel.Focus();
                using (var timer = new System.Windows.Forms.Timer { Interval = 180 })
                {
                    if (screenshot != null)
                    {
                        tabs.SelectedTab = appsTab;
                        timer.Tick += (s, e) => { timer.Stop(); if (force.Checked || !readings.Checked) throw new InvalidOperationException("Review defaults changed."); using (var image = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(image, new Rectangle(Point.Empty, dialog.Size)); image.Save(screenshot, System.Drawing.Imaging.ImageFormat.Png); } dialog.DialogResult = DialogResult.Cancel; };
                        dialog.Shown += (s, e) => timer.Start();
                    }
                    return dialog.ShowDialog(this) == DialogResult.OK;
                }
            }
        }
        async Task ApplySession(TuneSessionRequest request)
        {
            if (request.Settings.Items.Count != 0 && !backend.IsAdministrator) { Elevate(); return; }
            captureCancel = new CancellationTokenSource(); SetBusy(true, "Starting the reviewed tune…");
            // Keep configuration controls locked during writes. Closing the window
            // cancels observations and remaining app actions, never a setting write.
            content.Enabled = false;
            try
            {
                var progress = new Progress<string>(message => status.Text = message);
                var engine = new TuneSessionEngine(backend, journals, runningBackend,
                    token => demo ? CaptureResult.Demo() : CpuCapture.Run(8, token, null),
                    token => { if (!demo && token.WaitHandle.WaitOne(2000)) token.ThrowIfCancellationRequested(); });
                lastSession = await Task.Run(() => engine.Run(request, captureCancel.Token, message => ((IProgress<string>)progress).Report(message)));
                lastTune = lastSession.Summary() + ". Open Tune result for changes, observations and recovery.";
                selected.Clear();
                try { await RefreshAfterAction(); } catch (Exception ex) { lastSession.Notes.Add("Post-tune inventory refresh failed: " + Product.Clean(ex.Message, 300)); choicesStale = true; }
                ShowPage(14);
            }
            catch (Exception ex) { status.Text = "Tune stopped. Inspect Recovery before retrying."; Error(ex); }
            finally { captureCancel.Dispose(); captureCancel = null; SetBusy(false, lastSession == null ? "Tune stopped · inspect Recovery" : lastSession.Summary()); }
        }
        void SessionResultPage()
        {
            Heading("See what changed.", "Settings and app results, with short CPU / RAM / disk observations. No export or other app is required.");
            var body = new Panel(); Body(body);
            var summary = new Label { Text = lastSession == null ? "Apply a reviewed deep tune to see its result here." : lastSession.Summary() + (lastSession.Restored ? " · settings restored" : ""), ForeColor = Ink };
            var grid = Grid(); grid.ReadOnly = true;
            foreach (string name in new[] { "Resource", "Before", "After", "Observed difference" }) grid.Columns.Add(name, name);
            grid.Columns[0].FillWeight = 24; grid.Columns[1].FillWeight = grid.Columns[2].FillWeight = 17; grid.Columns[3].FillWeight = 42;
            if (lastSession != null) foreach (var row in TuneReadings.Compare(lastSession.Before, lastSession.After)) grid.Rows.Add(row.Name, row.Before, row.After, row.Difference);
            var details = new TextBox { Multiline = true, ReadOnly = true, BackColor = Color.White, ScrollBars = ScrollBars.Vertical,
                Text = lastSession == null ? "Deep tune combines saved configuration changes with reviewed companion closure. You can turn off its short resource checks in the review. Results stay in memory for this session; recovery receipts persist." : lastSession.Details() };
            var actions = new FlowLayoutPanel { WrapContents = true };
            actions.Controls.Add(ActionButton("Deep tune", (s, e) => ShowPage(11)));
            var restore = ActionButton("Restore this tune…", async (s, e) =>
            {
                if (busy || lastSession == null || lastSession.Settings == null || lastSession.Restored) return;
                if (!backend.IsAdministrator) { Elevate(); return; }
                if (!ReviewDialog("Restore this tune", new[] { "Group: " + lastSession.Settings.GroupId, "Saved settings are checked before restoration. Any independent edit stops group recovery. Closed apps and unsaved work cannot be restored by Lean81." }, "Restore the saved configuration from this tune. Reopen apps manually.", null, "Restore saved settings")) return;
                SetBusy(true, "Restoring this tune's saved settings…");
                try { await Task.Run(() => new DeepTuneEngine(backend, journals).Restore(lastSession.Settings.GroupId, null)); lastSession.Restored = true; await RefreshAfterAction(); ShowPage(14); SetBusy(false, "Tune settings restored · reopen apps manually"); }
                catch (Exception ex) { SetBusy(false, "Restoration stopped · inspect Recovery"); Error(ex); }
            });
            restore.Enabled = lastSession != null && lastSession.Settings != null && lastSession.Settings.Receipts.Count != 0 && !lastSession.Restored; actions.Controls.Add(restore);
            actions.Controls.Add(ActionButton("Recovery", (s, e) => ShowPage(7)));
            Rows(body, new Control[] { summary, grid, details, actions }, 42, 182, -1, 48);
        }
        internal void SmokeSessionScreenshots(string folder)
        {
            if (!demo) throw new InvalidOperationException("Synthetic mode required.");
            var settings = DeepTunePlan.Build(backend.Scan(UnusedProfile()), UnusedProfile(), true, false, true);
            var request = new TuneSessionRequest { Settings = settings, Apps = TuneSessionEngine.SuggestApps(runningBackend.Scan(), UnusedProfile()) };
            int appCount = request.Apps.Count;
            if (ReviewSession(request, Path.Combine(folder, "deep-review.png")) || request.Force || request.Apps.Count != appCount) throw new InvalidOperationException("Cancelled tune review mutated its request.");
            lastSession = new TuneSessionEngine(backend, journals, runningBackend, t => CaptureResult.Demo(), t => { }).Run(request, CancellationToken.None, null);
            if (lastSession.Status != "Applied" || lastSession.Settings == null || lastSession.Settings.Applied != settings.Items.Count || lastSession.Apps.Any(a => !a.Closed)) throw new InvalidOperationException("Combined tune did not complete.");
            foreach (var size in new[] { new Size(1080, 700), new Size(980, 620) }) { ClientSize = size; ShowPage(14); Refresh(); ValidateSmokeLayout(); SaveScreenshot(Path.Combine(folder, size.Width == 980 ? "tune-result-small.png" : "tune-result.png")); }
            new DeepTuneEngine(backend, journals).Restore(lastSession.Settings.GroupId, null); lastSession.Restored = true;
        }
        static UsageProfile UnusedProfile() { return Profiles.AllUnused(); }
    }
}
