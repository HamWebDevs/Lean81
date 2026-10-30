# Changelog

## 1.2.0-rc.3 · 2026-10-30

- Replace the all-or-nothing settings review with individual checkboxes, action timing, target details and tradeoffs. Companion closure remains a separate tab with normal-close defaults.
- Keep required service providers when an individual dependent is retained, including transitive dependencies. Selections only remove actions from a fresh plan; native policy and fresh-state checks still run before writes.
- Remember unchecked setting identities in a bounded local keep list. Show exclusions in the next review, allow individual re-inclusion, and clear them from Deep tune. Store only identity hashes, never startup commands or an executable plan.
- Allow saving keep choices without applying a Windows change. Cancel preserves both the proposed action plan and saved preferences. Keep-list changes do not restore settings that were already tuned.
- Add isolated selection, workspace migration, persistence, freshness and recovery tests plus actual Windows Forms review interaction/screenshots. Windows 8.1 hardware validation remains pending; publish as a preview.

## 1.2.0-rc.2 — interface preview

- Redesign the desktop interface in charcoal and lime with drawn icons, grouped navigation, consistent buttons, tables, tabs, charts and dialogs.
- Replace the long deep-tune checklist with Windows, Devices, Sharing and Apps groups, native accessible feature switches, three preset cards and an in-memory proposal summary.
- Mark manually edited preset choices as custom without changing saved answers or applying anything.
- Open on a snapshot dashboard with labelled CPU observations, available RAM and storage capacity, plus direct tuning and companion-closure shortcuts.
- Hide empty inventory review controls, constrain layout to the available footer height and fit the initial window to the desktop working area.
- Validate native accessibility actions, preset-card behavior, group navigation and minimum-window layouts. Keep rendering event driven, with no web view, downloaded assets or animation loop.

## 1.2.0-rc.1 — candidate

- Combine deep-tune settings and selected running-companion closure in one review. Individual app checkboxes, keep-all and explicit force-close remain available.
- Recheck app identity/foreground state before settings, apply saved settings first, and skip closure when settings fail. Cancellation leaves completed work in recovery.
- Show per-action results, direct tune-group restoration and optional short CPU/RAM/disk/commit observations inside Lean81, with coverage and power checks instead of an invented performance score.
- Offer already-disabled but still-running optional services, protect running disabled dependents, and restore both start type and running state.
- Add isolated orchestration/cancellation/measurement tests and a disposable native service fixture. Keep physical Windows 8.1 validation as a release gate.

## 1.1.0

- Make Deep tune the starting page and add a maximum-reduction browser/cloud-gaming preset.
- Expand optional services to 26 choices and include recognized unused startup/third-party-task categories in one review.
- Apply service dependency passes under a shared lease; group receipts and restore providers before dependents, with group conflict checks before writes.
- Move supported Startup-folder shortcuts to protected same-volume backups; restore original content, metadata, ownership and permission rules.
- Add reviewed same-user/session companion-app closure with creation-time, critical-process and foreground-family protections. Keep normal close and force close distinct.
- Add an opt-in existing High performance plan with AC preflight and original-scheme recovery.
- Preserve legacy receipts/workspaces and add isolated dependency/group/app/shortcut policy checks plus disposable native shortcut/process fixtures.

## 1.0.0

- Add Quick tune with a browser/cloud-gaming preset and an explicit combined review of unused services and optional desktop effects.
- Extend optional services to scanning, portable-device synchronization, error reporting and moved-file link tracking; retain usage/dependency protections.
- Add three allowlisted user desktop DWORD effects with exact original/absent-state journaling and conflict-aware recovery.
- Capture disk busy/throughput/queue, page-ins, commit usage, kernel CPU/pools/cache, and up to 20 CPU/memory/I/O process leaders per sample.
- Retain zero-CPU memory-heavy apps, keep reused PIDs separate and provide numeric resource sorting with qualified findings.
- Add CPU/RAM/disk chart lines with gaps for missing data, resource metrics/coverage in reports and compatible disk comparisons.
- Preserve existing workspaces, summaries and recovery receipts; new feature answers default to kept.
- Extend isolated resource/recovery tests, real native capture checks and GUI smoke interactions; keep Windows 8.1 hardware validation separate from modern Windows CI.

## 0.3.0 — preview

- Redesign local HTML reports with responsive cards, next steps, section navigation, accessible tables and print layout.
- Export a selected saved result with its baseline; reuse comparison checks and explain mismatched or incomplete observations without generating deltas.
- Add opt-in aggregate history, explicit sample/completion/power/observer coverage and separate scan/device/capture timestamps.
- Withhold workload labels and machine identities in every report mode. Keep software names opt-in, encode text and prohibit scripts/remote resources through content policy.
- Extend isolated report checks and GUI export-dialog cancellation; retain generated full/partial report evidence for browser review.
- Keep native Windows 8.1 targeting, recovery schemas and deliberate change controls. Physical Windows 8.1 validation is pending.

## 0.2.0 — preview

- Guided quick setup, remembered usage answers and three conservative presets.
- Plain-language recommendations, selectable-only filters, kept-item reasons and app search.
- Time-weighted CPU summaries, up to 12 saved results, a retained baseline and comparison condition checks.
- Device/RAM/uptime/power/battery and fixed-drive capacity snapshots.
- Local HTML reports, opt-in software names and formula-safe CSV capture export.
- Non-admin review, saveable text reviews, read-only recovery assessments and a help/tool hub.
- Eleven pages tested at normal and small window sizes, with setup/review cancellation, baseline comparison and checkbox/filter/protection interactions.
- Existing 0.1 receipts remain supported. Physical Windows 8.1 validation is still pending.

## 0.1.0 — preview

- Native C# / Windows Forms desktop app targeting Windows 8.1 and .NET Framework 4.5.1.
- Run startup inventory, third-party scheduled-task review and usage-based optional-service selections.
- Desktop app inventory and standard Windows tool links.
- On-demand CPU/process capture and ReclaimLab JSONL export, with process names omitted by default.
- Explicit review, private intent journals, exact-state verification and conflict-aware recovery.
- Synthetic demo, core transaction tests, isolated Windows fixtures, GUI screenshots and portable EXE packaging.

Physical Windows 8.1 testing is pending. No performance improvement is claimed without measurements on the target laptop.
