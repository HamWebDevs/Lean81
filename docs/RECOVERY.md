# Recovery

Each batch saves original settings in `%ProgramData%\Lean81` **before** the first write. The folder is restricted to Administrators and SYSTEM. Receipts contain exact registry strings, service start type/delayed/running state, task enabled state and a definition hash, or one of three allowlisted user DWORD values (including its original absence). They are private machine data, not support attachments.

Open Lean81 as the same Windows user, enable administrator mode, and select a receipt in **Recovery**. Review the items and choose **Restore settings**. A restore first checks every pending item against the saved original and expected disabled states. If anything changed independently, the entire restore preflight stops without writes. Lean81 never replaces a new startup command or a changed task definition with an old copy.

Apply is sequential. A failed item stops remaining changes; earlier successful changes remain recorded. Status **Needs review** means inspect the entry error. A crash may leave a receipt marked Applying or Restoring; the same conflict checks apply on the next launch. No claim of OS-level atomicity is made: Windows service, registry and task APIs do not provide one common transaction.

A partially completed service operation can match neither saved state. For example, its start type changed but Windows refused to stop it. Automatic recovery refuses this ambiguous state. Use `services.msc` to inspect the saved service name, start type and running state in the local receipt, and restore them manually only after understanding the error. Lean81 will not overwrite the mismatch. Back up this folder before manual repair; do not edit receipt files to force recovery.

Recovery is scoped to the machine and Windows user that made the changes. Alternate-account UAC elevation is refused so HKCU cannot silently switch to another account. Elevation starts a fresh scan and does not carry an untrusted change plan into the administrator process.

Directory redirections, unexpected storage owners/permissions, invalid receipt IDs, oversized JSON and unsupported actions are refused. Keep the recovery folder when moving the portable EXE. Removing the EXE does not undo settings; restore first if that is what you intend.

## Check before restoring

Version 0.2 adds **Check saved states**. It reads each saved item's current state and reports Already original, Matches saved disabled state, Conflict, or Unavailable. This assessment makes no system-setting changes. Restore still performs a fresh whole-receipt preflight; a previous assessment is not an authorization to overwrite a later edit.

Existing version 0.1 receipts remain supported because the receipt schema and earlier change kinds remain supported. Keep the protected ProgramData folder when upgrading or moving the EXE. Local workspace preferences and CPU summaries are separate from recovery receipts and cannot apply or restore settings.

## Desktop effects in 1.0

Desktop changes use the same protected journal and exact-state recovery engine. If a DWORD was absent, recovery removes only the value created by Lean81; if it existed, the exact original DWORD is restored. Existing keys are never recreated. A later independent edit stops recovery. Sign out and back in to refresh Explorer after applying or restoring effects.
