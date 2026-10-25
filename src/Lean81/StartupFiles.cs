using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Lean81
{
    public static class StartupFiles
    {
        const int MaxBytes = 16 * 1024;
        static readonly SecurityIdentifier Admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), SystemAccount = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        static string Scope { get { return Product.Hash(Environment.MachineName + "\n" + WindowsIdentity.GetCurrent().User.Value); } }
        public static string DirectoryFor(string scope)
        {
            if (scope != "user" && scope != "common") throw new InvalidOperationException("Unsupported startup folder scope.");
            string path = Environment.GetFolderPath(scope == "user" ? Environment.SpecialFolder.Startup : Environment.SpecialFolder.CommonStartup);
            if (String.IsNullOrEmpty(path)) throw new InvalidOperationException("The startup folder is unavailable.");
            return Path.GetFullPath(path);
        }
        static bool FileName(string value)
        {
            return Policy.ValidName(value) && value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !value.Contains("\\") && !value.Contains("/")
                && (value.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || value.EndsWith(".url", StringComparison.OrdinalIgnoreCase));
        }
        static void NoLinks(string directory)
        {
            for (var d = new DirectoryInfo(directory); d != null; d = d.Parent) if (d.Exists && (d.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("A startup path is redirected; inspect it manually.");
        }
        static string Full(ItemState identity)
        {
            if (identity == null || identity.Kind != "startupfile" || !FileName(identity.Target)) throw new InvalidOperationException("Unsupported startup shortcut.");
            string directory = DirectoryFor(identity.Scope); NoLinks(directory); return Path.Combine(directory, identity.Target);
        }
        public static ItemState Read(ItemState identity)
        {
            string path = Full(identity); return ReadAt(identity, path);
        }
        static ItemState ReadAt(ItemState identity, string path)
        {
            var state = new ItemState { Kind = "startupfile", Scope = identity.Scope, Target = identity.Target };
            if (!File.Exists(path)) return state;
            var attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.ReadOnly | FileAttributes.System | FileAttributes.Encrypted | FileAttributes.Offline)) != 0) throw new IOException("This shortcut has unsupported or protected file attributes.");
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (input.Length < 1 || input.Length > MaxBytes) throw new IOException("This shortcut exceeds the supported size.");
                var bytes = new byte[(int)input.Length]; int read = 0, n;
                while (read < bytes.Length && (n = input.Read(bytes, read, bytes.Length - read)) > 0) read += n;
                if (read != bytes.Length) throw new IOException("Shortcut changed while being read.");
                state.Exists = state.Enabled = true; state.Value = Convert.ToBase64String(bytes); state.ValueKind = 3; state.Definition = Product.Hash(state.Value);
                state.FileAttributes = (int)attributes; state.FileCreatedUtc = File.GetCreationTimeUtc(path).ToString("o", CultureInfo.InvariantCulture); state.FileModifiedUtc = File.GetLastWriteTimeUtc(path).ToString("o", CultureInfo.InvariantCulture);
                state.FileSecurity = File.GetAccessControl(path, AccessControlSections.Access | AccessControlSections.Owner | AccessControlSections.Group).GetSecurityDescriptorSddlForm(AccessControlSections.Access | AccessControlSections.Owner | AccessControlSections.Group);
            }
            return state;
        }
        public static string Text(ItemState state)
        {
            if (state.Value == null) return state.Target;
            var bytes = Convert.FromBase64String(state.Value);
            return state.Target + " " + Encoding.Unicode.GetString(bytes) + " " + Encoding.UTF8.GetString(bytes);
        }
        public static string SecurityFingerprint(string sddl)
        {
            if (sddl == null) return null;
            var descriptor = new RawSecurityDescriptor(sddl);
            byte[] dacl = descriptor.DiscretionaryAcl == null ? null : new byte[descriptor.DiscretionaryAcl.BinaryLength];
            if (dacl != null) descriptor.DiscretionaryAcl.GetBinaryForm(dacl, 0);
            // Auto-inheritance bookkeeping flags can change after a move. Preserve
            // owner, group, DACL protection and every ordered ACE in comparisons.
            return Product.Hash(Product.Json().Serialize(new { Owner = descriptor.Owner == null ? null : descriptor.Owner.Value, Group = descriptor.Group == null ? null : descriptor.Group.Value,
                Protected = (descriptor.ControlFlags & ControlFlags.DiscretionaryAclProtected) != 0, Dacl = dacl == null ? null : Convert.ToBase64String(dacl) }));
        }
        public static void Validate(ItemState state)
        {
            if (!FileName(state.Target) || state.Scope != "user" && state.Scope != "common" || !state.Exists || !state.Enabled || state.ValueKind != 3 || state.Value == null || state.Value.Length > 24 * 1024 || state.FileSecurity == null || state.FileSecurity.Length > 16384)
                throw new InvalidOperationException("This startup shortcut is outside the bounded folder policy.");
            byte[] bytes;
            try { bytes = Convert.FromBase64String(state.Value); } catch (FormatException) { throw new InvalidOperationException("Invalid shortcut backup."); }
            DateTime created, modified;
            if (bytes.Length < 1 || bytes.Length > MaxBytes || state.Definition != Product.Hash(state.Value)
                || !DateTime.TryParseExact(state.FileCreatedUtc, "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out created)
                || !DateTime.TryParseExact(state.FileModifiedUtc, "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out modified)
                || Policy.IsProtectedText(Text(state))) throw new InvalidOperationException("Protected or invalid startup shortcut.");
        }
        static string Backup(ItemState original)
        { return Path.Combine(new JournalStore(Scope).Quarantine(), Scope + "-" + Product.Hash(original.Identity() + "\n" + original.Definition) + ".bin"); }
        static bool SupportedOwner(string security)
        {
            var acl = new FileSecurity(); acl.SetSecurityDescriptorSddlForm(security);
            var owner = acl.GetOwner(typeof(SecurityIdentifier));
            return Admins.Equals(owner) || SystemAccount.Equals(owner) || WindowsIdentity.GetCurrent().User.Equals(owner);
        }
        static void CheckVolume(ItemState original)
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (!String.Equals(Path.GetPathRoot(Full(original)), Path.GetPathRoot(root), StringComparison.OrdinalIgnoreCase)) throw new IOException("This startup folder is on a different volume; use Windows Startup controls for it.");
        }
        public static void CheckAccess(ItemState original, bool restoring)
        {
            Validate(original); CheckVolume(original);
            if (!SupportedOwner(original.FileSecurity)) throw new InvalidOperationException("This shortcut has an unsupported owner; it stays available for manual review.");
            var now = Read(original);
            if (restoring && now.Fingerprint() == original.Fingerprint()) return;
            string backup = Backup(original);
            if (restoring)
            {
                if (now.Exists) throw new IOException("A new shortcut occupies the original name. It will not be overwritten.");
                var saved = ReadAt(original, backup);
                if (!saved.Exists || saved.Value != original.Value || saved.FileCreatedUtc != original.FileCreatedUtc || saved.FileModifiedUtc != original.FileModifiedUtc || saved.FileAttributes != original.FileAttributes) throw new IOException("Startup backup no longer matches the saved shortcut.");
            }
            else
            {
                if (now.Fingerprint() != original.Fingerprint() || File.Exists(backup)) throw new IOException("Shortcut or backup state changed. Rescan before changing it.");
            }
        }
        static void Private(string path)
        {
            var acl = new FileSecurity(); acl.SetAccessRuleProtection(true, false);
            foreach (var sid in new[] { Admins, SystemAccount }) acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, AccessControlType.Allow));
            var owner = File.GetAccessControl(path).GetOwner(typeof(SecurityIdentifier));
            if (!SystemAccount.Equals(owner)) acl.SetOwner(Admins);
            File.SetAccessControl(path, acl);
        }
        public static void Set(ItemState original, bool restoring)
        {
            CheckAccess(original, restoring); string source = Full(original), backup = Backup(original);
            if (restoring)
            {
                if (Read(original).Fingerprint() == original.Fingerprint()) return;
                var access = new FileSecurity(); access.SetSecurityDescriptorSddlForm(original.FileSecurity, AccessControlSections.Access);
                var wanted = new FileSecurity(); wanted.SetSecurityDescriptorSddlForm(original.FileSecurity);
                access.SetAccessRuleProtection(wanted.AreAccessRulesProtected, true);
                var currentOwner = File.GetAccessControl(backup).GetOwner(typeof(SecurityIdentifier));
                var wantedOwner = wanted.GetOwner(typeof(SecurityIdentifier));
                if (!currentOwner.Equals(wantedOwner)) access.SetOwner(wantedOwner);
                File.Move(backup, source); // Fails rather than replacing any new shortcut.
                // Apply inherited permissions in the original parent, not in the private backup directory.
                File.SetAccessControl(source, access);
            }
            else
            {
                File.Move(source, backup); // Preserve original file, ACL and timestamps on this volume.
                try
                {
                    var moved = ReadAt(original, backup);
                    // Windows can refresh inherited ACL flags when moving between parents.
                    // The original descriptor is already in the journal and is restored at
                    // the original location; backup integrity covers bytes and file metadata.
                    if (moved.Value != original.Value || moved.FileCreatedUtc != original.FileCreatedUtc || moved.FileModifiedUtc != original.FileModifiedUtc || moved.FileAttributes != original.FileAttributes)
                        throw new IOException("The moved shortcut's content or file metadata changed. Its file is retained for manual recovery.");
                }
                finally { Private(backup); }
            }
        }
    }
}
