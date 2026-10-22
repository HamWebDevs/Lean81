# Local data and privacy

Lean81 has no telemetry, upload endpoint, account, remote plugin loader, resident service or auto-update component. Inspection never runs startup commands, task actions or uninstall strings. Windows-tool shortcuts use a fixed catalog of system tools.

| Data | Location | Contents |
| --- | --- | --- |
| Preferences and CPU history | `%LocalAppData%\Lean81\workspace.json` | Usage answers, workload label, baseline ID and up to 12 numeric CPU summaries with hashed machine/hardware identities |
| Recovery receipts | `%ProgramData%\Lean81` | Private exact original settings, including startup command strings; restricted to Administrators and SYSTEM |
| Current process observations | App memory | Process names, IDs, CPU and working-set sizes for the active capture |
| Explicit exports | Your selected file | The format and name options you chose |

Preferences are ordinary local application data, not encrypted secrets. Workload labels can be personal: use generic labels when possible. Summary history contains no process names, process IDs, startup commands, receipt contents or file paths. Invalid or oversized settings are refused; they never become instructions to change Windows. The app never imports apply plans from the preferences file.

Demo mode uses synthetic data and in-memory settings/receipts. It does not write the production settings directory, change Windows or launch system tools.

HTML reports omit software names by default, encode optional names and contain no external resources. They do not export raw command/path/task/receipt fields, account data, workload labels, stored machine/hardware identities or scan error messages. Baseline comparisons use those identities only for local validation. Optional history contains numbered aggregate observations, not labels or IDs. Hardware descriptions, scan/device/capture timestamps and usage-derived next steps remain visible. Reports have no scripts; a restrictive content policy blocks remote resources and executable content. Explicitly included software names may still identify personal projects, so review before sharing.

JSONL traces omit process names by default, but include numeric process IDs and working-set sizes. CSV exports omit names by default and neutralize formula-leading names when names are included. Neither format includes startup commands or executable-path fields. A text review contains names and tradeoffs from the selected review, never an executable action plan.

To remove a CPU result, use **Saved results & baseline → Delete selected result**. To reset all preferences/history, close Lean81 and remove `workspace.json`. This does not undo Windows settings. To undo settings, use Recovery first. Do not delete the private recovery directory while you still need its original settings.
