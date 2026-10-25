using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace Lean81
{
    public static class Product
    {
        public const string Version = "1.1.0";
        public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const int MaxItems = 512;
        public const int MaxChanges = 40;
        public const int MaxJournalBytes = 2 * 1024 * 1024;
        public static JavaScriptSerializer Json()
        {
            return new JavaScriptSerializer { MaxJsonLength = MaxJournalBytes, RecursionLimit = 24 };
        }
        public static string Hash(string value)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""))).Replace("-", "").ToLowerInvariant();
        }
        public static string Clean(string value, int limit)
        {
            if (limit < 0) throw new ArgumentOutOfRangeException("limit");
            return new string((value ?? "").Where(c => !Char.IsControl(c)).Take(limit).ToArray());
        }
        public static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
    }

    public sealed class UsageProfile
    {
        public bool Printing { get; set; }
        public bool Bluetooth { get; set; }
        public bool IndexedSearch { get; set; }
        public bool TouchKeyboard { get; set; }
        public bool HomeGroup { get; set; }
        public bool MediaSharing { get; set; }
        public bool OfflineFiles { get; set; }
        public bool Prefetch { get; set; }
        public bool Scanning { get; set; }
        public bool PortableDevices { get; set; }
        public bool ErrorReporting { get; set; }
        public bool LinkTracking { get; set; }
        public bool Telemetry { get; set; }
        public bool MediaCenter { get; set; }
        public bool Compatibility { get; set; }
        public bool DiagnosticHelpers { get; set; }
        public bool Themes { get; set; }
        public bool AppUpdaters { get; set; }
        public bool GameLaunchers { get; set; }
        public bool CloudSync { get; set; }
        public bool ChatApps { get; set; }
        public bool MusicApps { get; set; }
        public UsageProfile()
        {
            Printing = Bluetooth = IndexedSearch = TouchKeyboard = HomeGroup = MediaSharing = OfflineFiles = Prefetch = true;
            Scanning = PortableDevices = ErrorReporting = LinkTracking = true;
            Telemetry = MediaCenter = Compatibility = DiagnosticHelpers = Themes = AppUpdaters = GameLaunchers = CloudSync = ChatApps = MusicApps = true;
        }
        public bool Uses(string feature)
        {
            switch (feature)
            {
                case "printing": return Printing;
                case "bluetooth": return Bluetooth;
                case "search": return IndexedSearch;
                case "touch": return TouchKeyboard;
                case "homegroup": return HomeGroup;
                case "media": return MediaSharing;
                case "offline": return OfflineFiles;
                case "prefetch": return Prefetch;
                case "scanning": return Scanning;
                case "portable": return PortableDevices;
                case "errors": return ErrorReporting;
                case "links": return LinkTracking;
                case "telemetry": return Telemetry;
                case "tv": return MediaCenter;
                case "compatibility": return Compatibility;
                case "diagnostics": return DiagnosticHelpers;
                case "themes": return Themes;
                case "updaters": return AppUpdaters;
                case "launchers": return GameLaunchers;
                case "sync": return CloudSync;
                case "chat": return ChatApps;
                case "music": return MusicApps;
                default: return true;
            }
        }
    }

    public sealed class ItemState
    {
        public string Kind { get; set; }
        public string Target { get; set; }
        public string Scope { get; set; }
        public bool Exists { get; set; }
        public bool Enabled { get; set; }
        public string Value { get; set; }
        public int ValueKind { get; set; }
        public int StartType { get; set; }
        public bool Delayed { get; set; }
        public bool Running { get; set; }
        public string Definition { get; set; }
        public string[] Dependents { get; set; }
        public int FileAttributes { get; set; }
        public string FileCreatedUtc { get; set; }
        public string FileModifiedUtc { get; set; }
        public string FileSecurity { get; set; }
        public ItemState() { Dependents = new string[0]; }
        public ItemState Copy() { return Product.Json().Deserialize<ItemState>(Product.Json().Serialize(this)); }
        public string Identity() { return Kind + "\n" + Scope + "\n" + Target; }
        public string Fingerprint()
        {
            return Product.Hash(Product.Json().Serialize(new {
                Kind, Target, Scope, Exists, Enabled, Value, ValueKind, StartType, Delayed, Running, Definition,
                // Dependents are a live eligibility observation, not changed settings.
                // Every service write still rechecks them in native preflight.
                Dependents = Kind == "service" ? new string[0] : (Dependents ?? new string[0]).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
                FileAttributes, FileCreatedUtc, FileModifiedUtc, FileSecurity
            }));
        }
        public ItemState Disabled()
        {
            var copy = Copy();
            if (Kind == "startup") { copy.Exists = false; copy.Enabled = false; copy.Value = null; copy.ValueKind = 0; }
            else if (Kind == "startupfile") { copy.Exists = false; copy.Enabled = false; copy.Value = null; copy.ValueKind = 0; copy.Definition = null; copy.FileAttributes = 0; copy.FileCreatedUtc = copy.FileModifiedUtc = copy.FileSecurity = null; }
            else if (Kind == "service") { copy.StartType = 4; copy.Enabled = false; copy.Running = false; copy.Delayed = false; }
            else if (Kind == "task") copy.Enabled = false;
            else if (Kind == "setting") { copy.Enabled = false; copy.Value = "0"; copy.ValueKind = 4; }
            else if (Kind == "power") { copy.Enabled = false; copy.Value = PowerPlans.HighPerformance; }
            else throw new InvalidOperationException("Unsupported change kind.");
            return copy;
        }
    }

    public sealed class ScanItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Kind { get; set; }
        public string Scope { get; set; }
        public string Status { get; set; }
        public string Detail { get; set; }
        public string Tradeoff { get; set; }
        public bool CanChange { get; set; }
        public bool Suggested { get; set; }
        public ItemState Before { get; set; }
        public string Feature { get; set; }
    }
    public sealed class InstalledApp
    {
        public string Name { get; set; }
        public string Publisher { get; set; }
        public string Version { get; set; }
        public string Note { get; set; }
    }
    public sealed class ScanReport
    {
        public List<ScanItem> Items { get; set; }
        public List<InstalledApp> Apps { get; set; }
        public List<string> Notes { get; set; }
        public string CapturedUtc { get; set; }
        public ScanReport()
        {
            Items = new List<ScanItem>(); Apps = new List<InstalledApp>(); Notes = new List<string>();
            CapturedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        }
    }
    public sealed class JournalEntry
    {
        public string Name { get; set; }
        public ItemState Original { get; set; }
        public ItemState Expected { get; set; }
        public string Status { get; set; }
        public string Error { get; set; }
    }
    public sealed class ChangeReceipt
    {
        public int Schema { get; set; }
        public string Id { get; set; }
        public string Version { get; set; }
        public string CreatedUtc { get; set; }
        public string MachineScope { get; set; }
        public string GroupId { get; set; }
        public int GroupSequence { get; set; }
        public string Status { get; set; }
        public List<JournalEntry> Entries { get; set; }
        public ChangeReceipt()
        {
            Schema = 1; Version = Product.Version; Id = Guid.NewGuid().ToString("N");
            CreatedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            Entries = new List<JournalEntry>(); Status = "Prepared";
        }
    }
    public interface IBackend
    {
        string MachineScope { get; }
        bool IsAdministrator { get; }
        ScanReport Scan(UsageProfile profile);
        ItemState Read(ItemState identity);
        void Preflight(ItemState original, UsageProfile profile, bool restoring);
        void Disable(ItemState original);
        void Restore(ItemState original);
    }
    public interface IJournalStore
    {
        IDisposable Lock();
        void Save(ChangeReceipt receipt, bool create);
        ChangeReceipt Load(string id);
        IList<ChangeReceipt> List();
    }
}
