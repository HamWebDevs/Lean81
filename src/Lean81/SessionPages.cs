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
        async Task ApplySession(TuneSessionRequest request)
        {
            if (request.Settings.Items.Count != 0 && !backend.IsAdministrator) { Elevate(); return; }
            lastSession = null;
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
            Heading("Tune result", "Settings and app results, with short CPU / RAM / disk observations. No export or other app is required.");
            var body = new Panel(); Body(body);
            var summary = new Label { Text = lastSession == null ? "Apply a reviewed deep tune to see its result here." : lastSession.Summary() + (lastSession.Restored ? " · settings restored" : ""), ForeColor = Ink };
            var grid = Grid(); grid.ReadOnly = true;
            foreach (string name in new[] { "Resource", "Before", "After", "Observed difference" }) grid.Columns.Add(name, name);
            grid.Columns[0].FillWeight = 24; grid.Columns[1].FillWeight = grid.Columns[2].FillWeight = 17; grid.Columns[3].FillWeight = 42;
            if (lastSession != null) foreach (var row in TuneReadings.Compare(lastSession.Before, lastSession.After)) grid.Rows.Add(row.Name, row.Before, row.After, row.Difference);
            var tabs = new DesignTabs(); var observations = new TabPage("Observations"); var remaining = new TabPage("Remaining load"); tabs.TabPages.Add(observations); tabs.TabPages.Add(remaining); observations.Controls.Add(grid);
            var remainingBody = new Panel { Dock = DockStyle.Fill }; remaining.Controls.Add(remainingBody);
            var processGrid = Grid(); processGrid.ReadOnly = true;
            foreach (string name in new[] { "Process / PID", "Peak CPU", "Peak RAM MiB", "Peak I/O MiB/s" }) processGrid.Columns.Add(name, name);
            processGrid.Columns[0].FillWeight = 42; processGrid.Columns[1].FillWeight = 18; processGrid.Columns[2].FillWeight = processGrid.Columns[3].FillWeight = 20;
            var tools = new FlowLayoutPanel(); tools.Controls.Add(new Label { Text = "After-check leaders · sort", AutoSize = true, Padding = new Padding(0, 7, 8, 0) });
            var sort = new DesignComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 145, Margin = new Padding(0, 4, 8, 0) }; sort.Items.AddRange(new object[] { "CPU", "Working-set RAM", "Read/write I/O" }); sort.SelectedIndex = 0; tools.Controls.Add(sort);
            Action populate = () =>
            {
                processGrid.Rows.Clear();
                var leaders = ResourceAnalysis.Processes(lastSession == null ? null : lastSession.After);
                foreach (var process in leaders.OrderByDescending(p => sort.SelectedIndex == 0 ? p.PeakCpu : sort.SelectedIndex == 1 ? p.PeakWorkingSet : p.PeakIo).Take(20))
                    processGrid.Rows.Add(process.Name + " / " + process.Pid, process.PeakCpu.ToString("0.0", CultureInfo.InvariantCulture) + "%", (process.PeakWorkingSet / 1048576).ToString("0.0", CultureInfo.InvariantCulture), process.HasIo ? (process.PeakIo / 1048576).ToString("0.0", CultureInfo.InvariantCulture) : "Unavailable");
            };
            sort.SelectedIndexChanged += (s, e) => populate(); populate();
            Rows(remainingBody, new Control[] { tools, processGrid }, 40, -1);
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
            actions.Controls.Add(ActionButton("Running apps", (s, e) => ShowPage(13)));
            actions.Controls.Add(ActionButton("Recovery", (s, e) => ShowPage(7)));
            Rows(body, new Control[] { summary, tabs, details, actions }, 42, 210, -1, 48);
        }
        internal void SmokeSessionScreenshots(string folder)
        {
            if (!demo) throw new InvalidOperationException("Synthetic mode required.");
            var settings = DeepTunePlan.Build(backend.Scan(UnusedProfile()), UnusedProfile(), true, false, true);
            var request = new TuneSessionRequest { Settings = settings, Apps = TuneSessionEngine.SuggestApps(runningBackend.Scan(), UnusedProfile()) };
            int appCount = request.Apps.Count;
            string keepBefore = Product.Json().Serialize(workspace.KeptTuneItems);
            if (ReviewSession(request, Path.Combine(folder, "deep-review.png")) || request.Force || request.Apps.Count != appCount) throw new InvalidOperationException("Cancelled tune review mutated its request.");
            if (keepBefore != Product.Json().Serialize(workspace.KeptTuneItems) || request.Settings != settings) throw new InvalidOperationException("Cancelled tune review saved keep choices or replaced its plan.");
            lastSession = new TuneSessionEngine(backend, journals, runningBackend, t => CaptureResult.Demo(), t => { }).Run(request, CancellationToken.None, null);
            if (lastSession.Status != "Applied" || lastSession.Settings == null || lastSession.Settings.Applied != settings.Items.Count || lastSession.Apps.Any(a => !a.Closed)) throw new InvalidOperationException("Combined tune did not complete.");
            status.Text = lastSession.Summary();
            foreach (var size in new[] { new Size(1080, 700), new Size(980, 620) }) { ClientSize = size; ShowPage(14); Refresh(); ValidateSmokeLayout(); SaveScreenshot(Path.Combine(folder, size.Width == 980 ? "tune-result-small.png" : "tune-result.png")); }
            var resultTabs = Descendants(content).OfType<TabControl>().Single(); resultTabs.SelectedIndex = 1;
            var resultSort = Descendants(content).OfType<ComboBox>().Single(); resultSort.SelectedIndex = 1;
            var resultGrid = Descendants(resultTabs.TabPages[1]).OfType<DataGridView>().Single();
            if (!Convert.ToString(resultGrid.Rows[0].Cells[0].Value, CultureInfo.InvariantCulture).StartsWith("IdleMemoryApp", StringComparison.Ordinal)) throw new InvalidOperationException("Memory-heavy after-check processes are missing from the result.");
            Refresh(); ValidateSmokeLayout(); SaveScreenshot(Path.Combine(folder, "tune-remaining-load.png"));
            new DeepTuneEngine(backend, journals).Restore(lastSession.Settings.GroupId, null); lastSession.Restored = true;
        }
        static UsageProfile UnusedProfile() { return Profiles.AllUnused(); }
    }
}
