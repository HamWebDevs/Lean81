using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace Lean81
{
    public sealed partial class MainForm
    {
        static readonly string[] FeatureTitles = { "Printing", "Bluetooth", "Search indexing", "Touch & handwriting", "HomeGroup", "Media sharing", "Offline network files", "App preloading", "Scanners & cameras", "Portable devices", "Error reports", "Moved-file links", "Experience reports", "Media Center TV", "Compatibility help", "Troubleshooting", "Windows themes", "App updaters", "Game launchers", "Cloud sync", "Chat apps", "Music apps" };
        static readonly string[] FeatureDescriptions = { "Includes PDF and virtual printers", "Discovery and paired devices", "Indexed file and email search", "Pen and on-screen keyboard", "Legacy home-network sharing", "Windows Media Player libraries", "Cached access to network files", "Superfetch / launch preloading", "Windows image acquisition", "Phone / media-library sync", "Windows diagnostic reports", "Track shortcuts to moved files", "Optional diagnostic tracking", "TV reception and recording", "Assistance for older software", "Automatic diagnostic helpers", "Keep the themed desktop", "Third-party update agents", "Steam, Epic and GOG companions", "OneDrive / Dropbox companions", "Discord, Skype, Teams and Slack", "Spotify / music companions" };
        static CheckBox[] OrderedFeatureChecks(Control root)
        { return Descendants(root).OfType<FeatureToggle>().OrderBy(c => c.FeatureIndex).Cast<CheckBox>().ToArray(); }
        FeatureToggle MakeFeature(int index, bool value)
        {
            return new FeatureToggle { FeatureIndex = index, Text = FeatureTitles[index], Description = FeatureDescriptions[index], Checked = value,
                AccessibleName = FeatureTitles[index], AccessibleDescription = "Checked means keep this feature. Unchecked means unused. " + FeatureLabels[index],
                Margin = new Padding(0), Width = 520, TabIndex = index };
        }
        static void FitFeatureList(FlowLayoutPanel host)
        {
            Action resize = () => { foreach (var child in host.Controls.OfType<FeatureToggle>()) child.Width = Math.Max(240, host.ClientSize.Width - host.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 2); };
            host.SizeChanged += (s, e) => resize(); resize();
        }
        Control GroupedFeatures(UsageProfile initial, out CheckBox[] checks)
        {
            var tabs = new DesignTabs { Dock = DockStyle.Fill }; var values = FeatureValues(initial); var result = new CheckBox[FeatureLabels.Length];
            var groups = new[] { new[] { 2, 7, 10, 11, 12, 14, 15, 16 }, new[] { 0, 1, 3, 8, 9 }, new[] { 4, 5, 6, 13 }, new[] { 17, 18, 19, 20, 21 } };
            string[] titles = { "Windows", "Devices", "Sharing", "Apps" };
            var tips = new ToolTip { AutoPopDelay = 12000, InitialDelay = 450, ReshowDelay = 100, ShowAlways = true };
            tabs.Disposed += (s, e) => tips.Dispose();
            for (int group = 0; group < groups.Length; group++)
            {
                var tab = new TabPage(titles[group]) { BackColor = Design.Surface, Padding = new Padding(4) }; tabs.TabPages.Add(tab);
                var list = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Design.Surface, Padding = new Padding(0, 2, 0, 4) }; tab.Controls.Add(list);
                foreach (int index in groups[group]) { var toggle = MakeFeature(index, values[index]); result[index] = toggle; list.Controls.Add(toggle); tips.SetToolTip(toggle, FeatureLabels[index] + ". Keep checked if used or uncertain."); }
                FitFeatureList(list);
            }
            checks = result; return tabs;
        }
        void QuickTunePage()
        {
            Heading("Deep tune", "Pick a starting point, then keep the features you use. Nothing changes until you review and apply.");
            var body = new Panel(); Body(body); CheckBox[] checks; var featureTabs = GroupedFeatures(profile, out checks);
            var presets = Presets(checks); presets.Width = 300;
            var presetRow = new TableLayoutPanel { ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            presetRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var tiles = new PresetControl[3]; int[] presetIndices = { 1, 4, 5 }; string[] tileTitles = { "Keep all", "Browser & cloud games", "Maximum reduction" }; string[] captions = { "Start with every feature kept", "A lighter everyday setup", "Most optional features unused" };
            for (int i = 0; i < 3; i++)
            {
                int index = i; presetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
                tiles[i] = new PresetControl { Text = tileTitles[i], Caption = captions[i], Dock = DockStyle.Fill, Margin = new Padding(0, 0, i == 2 ? 0 : 10, 0), AccessibleName = tileTitles[i], AccessibleDescription = captions[i] + ". Fills choices only; does not apply changes." };
                tiles[i].Click += (s, e) => presets.SelectedIndex = presetIndices[index]; presetRow.Controls.Add(tiles[i], i, 0);
            }
            var presetOptions = new FlowLayoutPanel(); presetOptions.Controls.Add(new Label { Text = "Starting point", AutoSize = true, ForeColor = Muted, Font = Design.Small, Padding = new Padding(0, 8, 12, 0) }); presetOptions.Controls.Add(presets);
            var middle = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            middle.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); middle.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
            featureTabs.Margin = new Padding(0, 0, 14, 0); featureTabs.Dock = DockStyle.Fill; middle.Controls.Add(featureTabs, 0, 0);
            var summary = new SurfacePanel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(12, 10, 12, 8) }; middle.Controls.Add(summary, 1, 0);
            var title = LabelAt("TUNE PREVIEW", 8.5f); title.ForeColor = Design.Accent;
            var counts = LabelAt("Scan to load the choices", 10.5f); counts.ForeColor = Ink;
            var effects = new CheckBox { Text = "Reduce desktop effects", AutoSize = true, Checked = true, Margin = new Padding(0, 4, 0, 3) };
            var startup = new CheckBox { Text = "Include startup items", AutoSize = true, Checked = true, Margin = new Padding(0, 4, 0, 3) };
            var closeApps = new CheckBox { Text = "Review running apps", AutoSize = true, Checked = true, Margin = new Padding(0, 4, 0, 3) };
            var power = new CheckBox { Text = "High performance plan", AutoSize = true, Margin = new Padding(0, 4, 0, 3) };
            var options = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false };
            options.Controls.Add(effects); options.Controls.Add(startup); options.Controls.Add(closeApps); options.Controls.Add(power);
            var tips = new ToolTip { AutoPopDelay = 12000 }; summary.Disposed += (s, e) => tips.Dispose();
            tips.SetToolTip(startup, "Recognized startup entries, shortcuts and third-party tasks in unused categories. Unknown entries need individual review.");
            tips.SetToolTip(closeApps, "Choose which recognized companions to close in the review. Normal close is the default; force close is explicit.");
            tips.SetToolTip(power, "Mains power is required. An existing High performance plan can use more heat and energy, and stays selected until changed or restored.");
            var safe = LabelAt("Security, networking and core Windows services stay protected.", 8.5f); safe.AutoEllipsis = true;
            Rows(summary, new Control[] { title, counts, options, safe }, 22, 80, 108, -1);
            var actionRow = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 10, 0, 0), Margin = Padding.Empty };
            actionRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            var note = LabelAt(lastTune ?? "KEEP = used or uncertain. UNUSED = eligible for this review.", 8.5f); note.AutoEllipsis = true; note.Dock = DockStyle.Fill; note.Padding = new Padding(0, 8, 14, 0); actionRow.Controls.Add(note, 0, 0);
            var apply = ActionButton("Review tune  →", async (s, e) =>
            {
                if (busy) return;
                bool includeEffects = effects.Checked, includeStartup = startup.Checked, includePower = power.Checked, includeApps = closeApps.Checked;
                Profiles.Copy(ReadChecks(checks), profile); SaveWorkspace(); if (!await Scan()) return;
                try
                {
                    var plan = DeepTunePlan.Build(report, profile, includeEffects, includePower, includeStartup);
                    var request = new TuneSessionRequest { Settings = plan, Apps = includeApps ? TuneSessionEngine.SuggestApps(runningApps, profile) : new System.Collections.Generic.List<RunningApp>() };
                    if (plan.Items.Count + request.Apps.Count == 0) { status.Text = "No matching settings or recognized companions. Check your kept features or Startup apps."; return; }
                    if (!ReviewSession(request)) return; await ApplySession(request);
                }
                catch (Exception ex) { SetBusy(false, "Tune stopped. Earlier changes, if any, are saved in Recovery."); Error(ex); }
            }, true); apply.Dock = DockStyle.Fill; apply.Margin = Padding.Empty; actionRow.Controls.Add(apply, 1, 0);
            Action refresh = () =>
            {
                foreach (var tile in tiles) { int index = Array.IndexOf(tiles, tile); tile.Selected = presets.SelectedIndex == presetIndices[index]; tile.Invalidate(); }
                if (report == null) { counts.Text = "Scan to load the choices"; return; }
                try
                {
                    var answers = ReadChecks(checks); var plan = DeepTunePlan.Build(report, answers, effects.Checked, power.Checked, startup.Checked);
                    int apps = closeApps.Checked ? TuneSessionEngine.SuggestApps(runningApps, answers).Count : 0;
                    counts.Text = plan.Items.Count(i => i.Kind == "service").ToString("00", CultureInfo.InvariantCulture) + "   services\r\n" + plan.Items.Count(i => i.Kind == "startup" || i.Kind == "startupfile" || i.Kind == "task").ToString("00", CultureInfo.InvariantCulture) + "   startup items\r\n" + apps.ToString("00", CultureInfo.InvariantCulture) + "   running processes\r\n" + plan.Items.Count(i => i.Kind == "setting" || i.Kind == "power").ToString("00", CultureInfo.InvariantCulture) + "   desktop / power";
                    if (presets.SelectedIndex == 5) note.Text = "Maximum reduction can affect printing, Bluetooth and launch times. Disabled app updaters need manual updates. Review every choice.";
                    else note.Text = lastTune ?? "KEEP = used or uncertain. UNUSED = eligible for this review.";
                }
                catch (InvalidOperationException) { counts.Text = "Too many choices.\r\nReview smaller groups."; }
            };
            foreach (var check in checks.Concat(new[] { effects, startup, closeApps, power })) check.CheckedChanged += (s, e) => refresh();
            presets.SelectedIndexChanged += (s, e) => refresh(); refresh();
            Rows(body, new Control[] { presetRow, presetOptions, middle, actionRow }, 78, 42, -1, 58);
        }
    }
}
