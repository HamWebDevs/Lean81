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
        public const string Version = "0.2.0";
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
            var text = new string((value ?? "").Where(c => !Char.IsControl(c)).ToArray());
            return text.Length > limit ? text.Substring(0, limit) : text;
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
        public UsageProfile()
        {
            Printing = Bluetooth = IndexedSearch = TouchKeyboard = HomeGroup = MediaSharing = OfflineFiles = Prefetch = true;
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
        public ItemState() { Dependents = new string[0]; }
        public ItemState Copy() { return Product.Json().Deserialize<ItemState>(Product.Json().Serialize(this)); }
        public string Identity() { return Kind + "\n" + Scope + "\n" + Target; }
        public string Fingerprint()
        {
            return Product.Hash(Product.Json().Serialize(new {
                Kind, Target, Scope, Exists, Enabled, Value, ValueKind, StartType, Delayed, Running, Definition,
                Dependents = (Dependents ?? new string[0]).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray()
            }));
        }
        public ItemState Disabled()
        {
            var copy = Copy();
            if (Kind == "startup") { copy.Exists = false; copy.Enabled = false; copy.Value = null; copy.ValueKind = 0; }
            else if (Kind == "service") { copy.StartType = 4; copy.Enabled = false; copy.Running = false; copy.Delayed = false; }
            else if (Kind == "task") copy.Enabled = false;
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
