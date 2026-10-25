using System;
using System.Linq;
using Microsoft.Win32;

namespace Lean81
{
    public sealed class DesktopOption
    {
        public string Id, KeyPath, ValueName, Title, Tradeoff;
        public DesktopOption(string id, string keyPath, string valueName, string title, string tradeoff) { Id = id; KeyPath = keyPath; ValueName = valueName; Title = title; Tradeoff = tradeoff; }
    }
    public static class DesktopSettings
    {
        const string AdvancedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        const string DwmPath = @"Software\Microsoft\Windows\DWM";
        public static readonly DesktopOption[] Options = {
            new DesktopOption("taskbar-animation", AdvancedPath, "TaskbarAnimations", "Taskbar animations", "Taskbar animation effects are reduced. Sign out and back in to fully refresh the desktop."),
            new DesktopOption("desktop-peek", DwmPath, "EnableAeroPeek", "Desktop Peek effect", "Hover-to-preview desktop/window Peek is disabled. Sign out and back in to refresh the desktop."),
            new DesktopOption("icon-shadows", AdvancedPath, "ListviewShadow", "Desktop icon text shadows", "Desktop icon label shadows are removed, which may change readability on your wallpaper. Sign out and back in to refresh the desktop.")
        };
        static DesktopOption Option(ItemState identity)
        {
            var option = identity == null ? null : Options.FirstOrDefault(o => o.Id == identity.Target);
            if (option == null || identity.Kind != "setting" || identity.Scope != "user") throw new InvalidOperationException("Unsupported desktop setting.");
            return option;
        }
        public static ItemState Read(ItemState identity)
        {
            var option = Option(identity); var state = new ItemState { Kind = "setting", Scope = "user", Target = option.Id };
            using (var key = Registry.CurrentUser.OpenSubKey(option.KeyPath))
            {
                state.Exists = key != null;
                if (key == null) return state;
                if (key.GetValueNames().Contains(option.ValueName, StringComparer.OrdinalIgnoreCase))
                {
                    state.ValueKind = (int)key.GetValueKind(option.ValueName);
                    if (state.ValueKind == 4) state.Value = ((int)key.GetValue(option.ValueName)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                state.Enabled = state.ValueKind != 4 || state.Value != "0";
            }
            return state;
        }
        public static void CheckAccess(ItemState state)
        {
            var option = Option(state);
            using (var key = Registry.CurrentUser.OpenSubKey(option.KeyPath, true)) if (key == null) throw new InvalidOperationException("The existing desktop settings key is missing; it will not be recreated.");
        }
        public static void Set(ItemState original, bool restore)
        {
            var option = Option(original);
            Policy.Validate(original, new UsageProfile(), restore);
            using (var key = Registry.CurrentUser.OpenSubKey(option.KeyPath, true))
            {
                if (key == null) throw new InvalidOperationException("Desktop settings location disappeared.");
                if (restore && original.ValueKind == 0) key.DeleteValue(option.ValueName, false);
                else key.SetValue(option.ValueName, restore ? Int32.Parse(original.Value, System.Globalization.CultureInfo.InvariantCulture) : 0, RegistryValueKind.DWord);
                key.Flush();
            }
        }
    }
}
