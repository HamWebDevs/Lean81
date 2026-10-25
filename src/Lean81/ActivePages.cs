using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Lean81
{
    public sealed partial class MainForm
    {
        readonly IRunningApps runningBackend;
        IList<RunningApp> runningApps = new List<RunningApp>();
        string lastClose;
        async Task RefreshAfterAction()
        {
            report = await Task.Run(() => backend.Scan(profile));
            device = await Task.Run(() => demo ? DeviceSnapshot.Demo() : DeviceSnapshot.Read());
            runningApps = await Task.Run(() => runningBackend.Scan()); choicesStale = false; selected.Clear();
        }
        void RunningAppsPage()
        {
            Heading("Free resources now.", "Close selected launcher, sync, chat or music companions. Browsers, Windows, drivers and foreground app families are kept.");
            var body = new Panel(); Body(body);
            var grid = Grid(); grid.Columns.Add(new DataGridViewCheckBoxColumn { Width = 36, AutoSizeMode = DataGridViewAutoSizeColumnMode.None });
            grid.Columns.Add("Name", "Recognized app / PID"); grid.Columns.Add("Memory", "RAM MiB"); grid.Columns.Add("State", "Current state");
            for (int i = 1; i < grid.Columns.Count; i++) grid.Columns[i].ReadOnly = true;
            grid.Columns[1].FillWeight = 48; grid.Columns[2].FillWeight = 18; grid.Columns[3].FillWeight = 34;
            foreach (var app in runningApps.OrderBy(a => a.Foreground).ThenByDescending(a => a.Memory))
            {
                var row = grid.Rows[grid.Rows.Add(false, app.Name + " / " + app.Pid, (app.Memory / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture), app.Foreground ? "Foreground family · kept" : "Available for your review")];
                row.Tag = app; row.Cells[0].ReadOnly = app.Foreground; row.Cells[1].ToolTipText = app.Path;
            }
            grid.CurrentCellDirtyStateChanged += (s, e) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            var force = new CheckBox { Text = "Force close selected apps if they stay running after a normal close request", AutoSize = true };
            var detail = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.White,
                Text = lastClose ?? "Select only companions you do not need right now. Closing can interrupt sync, downloads, music, calls or unsaved app state. Force close also ends tray/helper processes. Closed apps are reopened manually; Recovery restores settings, not app work. RAM readings are working sets and can include shared pages." };
            grid.SelectionChanged += (s, e) => { if (grid.SelectedRows.Count > 0) { var app = grid.SelectedRows[0].Tag as RunningApp; if (app != null) detail.Text = app.Name + "\r\n" + app.Path + "\r\n" + (app.Foreground ? "Kept: this app family was in the foreground at scan time." : "Closing may interrupt this app's work. Your current user, process identity and foreground state are checked again before sending a close request.") + (lastClose == null ? "" : "\r\n\r\n" + lastClose); } };
            var actions = new FlowLayoutPanel { WrapContents = true };
            actions.Controls.Add(ActionButton("Select background apps", (s, e) => { foreach (DataGridViewRow row in grid.Rows) if (!row.Cells[0].ReadOnly) row.Cells[0].Value = true; }));
            actions.Controls.Add(ActionButton("Close selected…", async (s, e) =>
            {
                if (busy) return; grid.EndEdit();
                var chosen = grid.Rows.Cast<DataGridViewRow>().Where(r => !r.Cells[0].ReadOnly && Convert.ToBoolean(r.Cells[0].Value)).Select(r => (RunningApp)r.Tag).ToList();
                if (chosen.Count == 0) { status.Text = "Select the apps you want to close first."; return; }
                bool forcibly = force.Checked;
                if (!ReviewDialog("Close selected background apps", chosen.Select(a => a.Name + " / " + a.Pid + "\r\n" + a.Path).ToArray(), forcibly ? "Selected apps may be force closed, interrupting sync, downloads, calls or unsaved app state. Reopen them manually afterwards. Windows, browsers and foreground families are protected." : "Lean81 will request a normal close. Tray/helper apps may remain running; force close is a separate option. Reopen closed apps manually.", null, forcibly ? "Close / force selected apps" : "Request selected apps close")) return;
                SetBusy(true, "Closing the reviewed background apps…");
                try
                {
                    var results = await Task.Run(() => AppCatalog.CloseSelected(runningBackend, chosen, forcibly));
                    lastClose = String.Join(Environment.NewLine, results.Select(r => r.Name + " / " + r.Pid + ": " + r.Status));
                    int closed = results.Count(r => r.Closed); await RefreshAfterAction(); ShowPage(13);
                    SetBusy(false, closed + " processes closed or already exited · " + (results.Count - closed) + " kept / still running");
                }
                catch (Exception ex) { SetBusy(false, "App closure stopped; refresh to inspect current apps."); Error(ex); }
            }, true));
            actions.Controls.Add(ActionButton("Refresh", async (s, e) =>
            {
                if (busy) return; SetBusy(true, "Refreshing recognized background apps…");
                try { runningApps = await Task.Run(() => runningBackend.Scan()); ShowPage(13); }
                catch (Exception ex) { Error(ex); } finally { SetBusy(false, "Background app list refreshed"); }
            }));
            Rows(body, new Control[] { grid, force, detail, actions }, -1, 36, 105, 88);
        }
        internal void SmokeActiveActions()
        {
            if (!demo) throw new InvalidOperationException("Synthetic mode required.");
            ShowPage(11); var presets = Descendants(content).OfType<ComboBox>().Single(); presets.SelectedIndex = 5;
            var answers = ReadChecks(Descendants(content).OfType<CheckBox>().Take(FeatureLabels.Length).ToArray());
            if (Product.Json().Serialize(answers) != Product.Json().Serialize(Profiles.Preset(4))) throw new InvalidOperationException("Maximum-reduction answers are not connected to the deep-tune page.");
            var initial = report.Items.ToDictionary(i => i.Id, i => backend.Read(i.Before).Fingerprint());
            var engine = new DeepTuneEngine(backend, journals);
            var tune = engine.Apply(DeepTunePlan.Build(backend.Scan(answers), answers, true, false, true), null);
            if (tune.Status != "Applied" || tune.Receipts.Count < 2 || engine.Restore(tune.GroupId, null) != tune.Applied) throw new InvalidOperationException("Demo dependency tune/group restore failed.");
            foreach (var item in report.Items) if (backend.Read(item.Before).Fingerprint() != initial[item.Id]) throw new InvalidOperationException("Demo group restore did not return the original settings.");
            ShowPage(13); var grid = Descendants(content).OfType<DataGridView>().Single();
            if (grid.Rows.Count == 0 || grid.Rows.Cast<DataGridViewRow>().Any(r => ((RunningApp)r.Tag).Foreground && !r.Cells[0].ReadOnly)) throw new InvalidOperationException("Foreground companion protection is missing.");
            var selectedApp = runningApps.First(a => !a.Foreground);
            var results = AppCatalog.CloseSelected(runningBackend, new[] { selectedApp }, true);
            if (!results.Single().Closed || runningBackend.Scan().Any(a => a.Pid == selectedApp.Pid)) throw new InvalidOperationException("Demo app closure is not connected to the running-app backend.");
        }
    }
}
