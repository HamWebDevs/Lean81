using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Lean81
{
    public sealed class ServiceOption
    {
        public string Name, Feature, Title, Tradeoff;
        public ServiceOption(string name, string feature, string title, string tradeoff)
        { Name = name; Feature = feature; Title = title; Tradeoff = tradeoff; }
    }
    public static class Policy
    {
        public static readonly ServiceOption[] Services = {
            new ServiceOption("Spooler", "printing", "Printing and PDF printers", "Printing, including virtual PDF printers, will be unavailable until restored."),
            new ServiceOption("bthserv", "bluetooth", "Bluetooth devices", "Bluetooth discovery and paired-device features can stop working."),
            new ServiceOption("WSearch", "search", "Indexed file and email search", "Search can be slower or incomplete in apps that use the Windows index."),
            new ServiceOption("TabletInputService", "touch", "Touch, pen and on-screen keyboard", "Touch keyboard, handwriting and pen features can be unavailable."),
            new ServiceOption("HomeGroupListener", "homegroup", "HomeGroup listener", "HomeGroup sharing features will be unavailable."),
            new ServiceOption("HomeGroupProvider", "homegroup", "HomeGroup provider", "HomeGroup discovery/sharing features will be unavailable."),
            new ServiceOption("WMPNetworkSvc", "media", "Media-player network sharing", "Other devices will no longer discover shared Windows Media Player libraries."),
            new ServiceOption("CscService", "offline", "Offline network files", "Cached offline access to network files will be unavailable."),
            new ServiceOption("SysMain", "prefetch", "Application preloading / Superfetch", "Disabling preloading may slow application launches. Measure the tradeoff on this laptop.")
        };
        static readonly Regex Protected = new Regex(@"(?i)(defender|securityhealth|antivirus|msascui|mpcmdrun|smartscreen|avast|avira|bitdefender|kaspersky|malwarebytes|mcafee|norton|symantec|sophos|webroot|trendmicro|eset|syntp|etdctrl|touchpad|hotkey|igfxtray|hkcmd)", RegexOptions.Compiled);
        public static bool IsProtectedText(string value) { return Protected.IsMatch(value ?? ""); }
        public static bool ValidName(string value) { return !String.IsNullOrWhiteSpace(value) && value.Length <= 256 && !value.Any(Char.IsControl); }
        public static bool ValidTaskPath(string value)
        {
            return !String.IsNullOrEmpty(value) && value.Length <= 1024 && value[0] == '\\' && !value.EndsWith("\\", StringComparison.Ordinal)
                && value.IndexOf("..", StringComparison.Ordinal) < 0 && !value.Any(Char.IsControl)
                && value.IndexOf('/', 0) < 0 && value.IndexOf(':', 0) < 0;
        }
        public static void Validate(ItemState state, UsageProfile profile, bool restoring)
        {
            if (state == null || !ValidName(state.Target) && state.Kind != "task") throw new InvalidOperationException("Invalid change identity.");
            if (state.Kind == "service")
            {
                var option = Services.FirstOrDefault(s => String.Equals(s.Name, state.Target, StringComparison.OrdinalIgnoreCase));
                if (option == null || state.Scope != "machine" || !state.Exists || state.StartType < 2 || state.StartType > 4 || String.IsNullOrEmpty(state.Definition) || state.Definition.Length != 64)
                    throw new InvalidOperationException("This service is outside Lean81's optional-service policy.");
                if (!restoring && profile.Uses(option.Feature)) throw new InvalidOperationException("Your usage profile keeps this feature enabled.");
                if (!restoring && (state.Dependents ?? new string[0]).Length != 0)
                    throw new InvalidOperationException("A non-disabled dependent service needs this service. No dependent services will be stopped.");
            }
            else if (state.Kind == "startup")
            {
                if (!new[] { "user", "machine32", "machine64" }.Contains(state.Scope) || state.Target.IndexOf('\\') >= 0 || state.Target.IndexOf('/') >= 0)
                    throw new InvalidOperationException("This startup location is outside the supported Run keys.");
                if (state.Exists && (state.Value == null || state.Value.Length > 8192 || state.ValueKind != 1 && state.ValueKind != 2))
                    throw new InvalidOperationException("Only bounded string/expand-string startup values can be changed.");
                if (IsProtectedText(state.Target + " " + state.Value)) throw new InvalidOperationException("This appears to be a security or hardware-input startup item and is protected.");
            }
            else if (state.Kind == "task")
            {
                if (!ValidTaskPath(state.Target) || state.Scope != "machine" || !state.Exists
                    || state.Target.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase)
                    || IsProtectedText(state.Target) || String.IsNullOrEmpty(state.Definition) || state.Definition.Length != 64)
                    throw new InvalidOperationException("System, security and unsupported scheduled tasks are inspection-only.");
            }
            else throw new InvalidOperationException("Unsupported change kind.");
        }
        public static string StateLabel(ItemState state)
        {
            if (!state.Exists) return "Absent";
            if (state.Kind == "service") return (state.StartType == 4 ? "Disabled" : state.StartType == 2 ? "Automatic" : "Manual") + (state.Running ? " / running" : " / stopped");
            return state.Enabled ? "Enabled" : "Disabled";
        }
    }
}
