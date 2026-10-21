using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Lean81
{
    public sealed class JournalStore : IJournalStore
    {
        readonly string directory, scope;
        static readonly SecurityIdentifier Admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        static readonly SecurityIdentifier SystemAccount = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        public JournalStore(string machineScope)
        {
            scope = machineScope;
            directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Lean81");
        }
        static bool Trusted(IdentityReference id) { return Admins.Equals(id) || SystemAccount.Equals(id); }
        static void NoLinks(string path)
        {
            for (var item = new DirectoryInfo(path); item != null; item = item.Parent)
                if (item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Recovery path contains a redirected directory.");
        }
        void EnsureDirectory()
        {
            if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) throw new InvalidOperationException("Enable administrator mode to access private recovery receipts.");
            NoLinks(directory);
            if (!Directory.Exists(directory))
            {
                var acl = new DirectorySecurity(); acl.SetAccessRuleProtection(true, false); acl.SetOwner(Admins);
                foreach (var id in new[] { Admins, SystemAccount }) acl.AddAccessRule(new FileSystemAccessRule(id, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                Directory.CreateDirectory(directory, acl);
            }
            NoLinks(directory);
            var security = Directory.GetAccessControl(directory, AccessControlSections.Owner | AccessControlSections.Access);
            if (!security.AreAccessRulesProtected || !Trusted(security.GetOwner(typeof(SecurityIdentifier)))) throw new InvalidOperationException("Recovery folder has an unexpected owner or inherited permissions. No changes were made.");
            CheckRules(security);
        }
        static void CheckRules(FileSystemSecurity security)
        {
            bool admin = false, system = false;
            foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType != AccessControlType.Allow || !Trusted(rule.IdentityReference)) throw new InvalidOperationException("Recovery storage permissions are not private to administrators and SYSTEM.");
                if ((rule.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl)
                { admin |= Admins.Equals(rule.IdentityReference); system |= SystemAccount.Equals(rule.IdentityReference); }
            }
            if (!admin || !system) throw new InvalidOperationException("Recovery storage is missing required administrative permissions.");
        }
        void CheckFile(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Recovery file is redirected.");
            var acl = File.GetAccessControl(path, AccessControlSections.Owner | AccessControlSections.Access);
            if (!Trusted(acl.GetOwner(typeof(SecurityIdentifier)))) throw new InvalidOperationException("Recovery file has an unexpected owner.");
            CheckRules(acl);
        }
        static void SetNewOwner(string path)
        {
            var acl = new FileSecurity(); acl.SetOwner(Admins); File.SetAccessControl(path, acl);
        }
        string ReceiptPath(string id)
        {
            Guid guid; if (!Guid.TryParseExact(id, "N", out guid)) throw new InvalidOperationException("Invalid receipt ID.");
            return Path.Combine(directory, guid.ToString("N") + ".json");
        }
        public IDisposable Lock()
        {
            EnsureDirectory(); var path = Path.Combine(directory, "session.lock");
            bool existed = File.Exists(path); if (existed) CheckFile(path);
            var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            try { if (!existed) SetNewOwner(path); CheckFile(path); return stream; } catch { stream.Dispose(); throw; }
        }
        public void Save(ChangeReceipt receipt, bool create)
        {
            EnsureDirectory(); ChangeEngine.ValidateReceipt(receipt, scope);
            string path = ReceiptPath(receipt.Id);
            if (File.Exists(path)) { CheckFile(path); if (create) throw new InvalidOperationException("Receipt already exists."); }
            else if (!create) throw new InvalidOperationException("Receipt disappeared. Changes stopped.");
            byte[] bytes = new UTF8Encoding(false).GetBytes(Product.Json().Serialize(receipt));
            if (bytes.Length > Product.MaxJournalBytes) throw new InvalidOperationException("Receipt is too large.");
            string temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(bytes, 0, bytes.Length); file.Flush(true); }
                SetNewOwner(temporary); CheckFile(temporary);
                if (create) File.Move(temporary, path); else File.Replace(temporary, path, null);
                CheckFile(path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public ChangeReceipt Load(string id)
        {
            EnsureDirectory(); string path = ReceiptPath(id); CheckFile(path);
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (file.Length > Product.MaxJournalBytes) throw new InvalidOperationException("Receipt exceeds the size limit.");
                using (var reader = new StreamReader(file, Encoding.UTF8, true))
                {
                    var receipt = Product.Json().Deserialize<ChangeReceipt>(reader.ReadToEnd()); ChangeEngine.ValidateReceipt(receipt, scope);
                    if (receipt.Id != id) throw new InvalidOperationException("Receipt identity mismatch."); return receipt;
                }
            }
        }
        public IList<ChangeReceipt> List()
        {
            EnsureDirectory(); var result = new List<ChangeReceipt>();
            foreach (var path in Directory.EnumerateFiles(directory, "*.json").Take(500)) result.Add(Load(Path.GetFileNameWithoutExtension(path)));
            return result.OrderByDescending(r => r.CreatedUtc, StringComparer.Ordinal).ToList();
        }
    }
    // Demo and unit tests never touch the machine journal or system settings.
    public sealed class MemoryJournals : IJournalStore
    {
        readonly Dictionary<string, string> data = new Dictionary<string, string>();
        sealed class Lease : IDisposable { public void Dispose() { } }
        public IDisposable Lock() { return new Lease(); }
        public void Save(ChangeReceipt receipt, bool create)
        {
            if (create == data.ContainsKey(receipt.Id)) throw new InvalidOperationException("Invalid journal create/update.");
            data[receipt.Id] = Product.Json().Serialize(receipt);
        }
        public ChangeReceipt Load(string id) { return Product.Json().Deserialize<ChangeReceipt>(data[id]); }
        public IList<ChangeReceipt> List() { return data.Keys.Select(Load).OrderByDescending(r => r.CreatedUtc).ToList(); }
    }
}
