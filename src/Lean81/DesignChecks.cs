using System;
using System.Linq;
using System.Windows.Forms;

namespace Lean81
{
    public sealed partial class MainForm
    {
        internal void SmokeDesignInteractions()
        {
            if (!demo) throw new InvalidOperationException("Synthetic UI required.");
            ShowPage(11); string original = Product.Json().Serialize(profile);
            var tiles = Descendants(content).OfType<PresetControl>().ToArray();
            tiles.Single(t => t.Text == "Maximum reduction").PerformClick();
            var checks = OrderedFeatureChecks(content); var presets = Descendants(content).OfType<ComboBox>().Single();
            if (checks.Length != FeatureLabels.Length || Product.Json().Serialize(ReadChecks(checks)) != Product.Json().Serialize(Profiles.Preset(4)) || original != Product.Json().Serialize(profile))
                throw new InvalidOperationException("Preset cards do not stage the correct choices independently.");
            var tabs = Descendants(content).OfType<DesignTabs>().Single(); tabs.SelectedIndex = 0; Refresh();
            var search = (FeatureToggle)checks[2]; bool before = search.Checked;
            search.AccessibilityObject.DoDefaultAction();
            if (search.Checked == before || presets.SelectedIndex != 0 || String.IsNullOrEmpty(search.AccessibilityObject.Name) || search.AccessibleRole != AccessibleRole.CheckButton || !search.TabStop)
                throw new InvalidOperationException("The feature switch lost its native checkbox action or custom-choice state.");
            if (tiles.Any(t => t.Selected)) throw new InvalidOperationException("A manually edited profile still claims to match a preset.");
            for (int i = 0; i < tabs.TabCount; i++)
            {
                tabs.SelectedIndex = i; Refresh(); ValidateSmokeLayout();
                foreach (var toggle in Descendants(tabs.TabPages[i]).OfType<FeatureToggle>())
                    if (toggle.Width > toggle.Parent.ClientSize.Width || toggle.Height < 50 || String.IsNullOrEmpty(toggle.AccessibleDescription)) throw new InvalidOperationException("A grouped feature switch is clipped or lacks its description.");
            }
            if (navigation.Count != Pages.Length || navigation.Any(b => !(b is NavControl) || String.IsNullOrEmpty(b.AccessibleName))) throw new InvalidOperationException("Grouped navigation lost a page or accessible name.");
            if (review.Visible || !localBadge.Visible) throw new InvalidOperationException("An empty review still crowds the footer.");
            if (original != Product.Json().Serialize(profile)) throw new InvalidOperationException("Design checks mutated saved choices.");
        }
    }
}
