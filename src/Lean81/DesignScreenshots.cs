using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Lean81
{
    public sealed partial class MainForm
    {
        internal void SmokeDesignScreenshots(string folder)
        {
            if (!demo) throw new InvalidOperationException("Synthetic UI required.");
            foreach (var size in new[] { new Size(1080, 700), new Size(980, 620) })
            {
                ClientSize = size; ShowPage(11);
                Descendants(content).OfType<PresetControl>().Single(c => c.Text == "Maximum reduction").PerformClick();
                Refresh(); ValidateSmokeLayout(); SaveScreenshot(Path.Combine(folder, size.Width == 980 ? "design-tune-small.png" : "design-tune.png"));
            }
            ShowPage(0); Refresh(); ValidateSmokeLayout(); SaveScreenshot(Path.Combine(folder, "design-dashboard.png"));
        }
    }
}
