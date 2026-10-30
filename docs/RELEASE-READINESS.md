# Releasing the 1.2 candidate

The stable public build remains 1.1. Candidate source and Actions packages use 1.2.0-rc.3. Do not describe modern Windows CI or synthetic screenshots as testing on Windows 8.1 hardware.

## Automated evidence

Run `scripts/build.ps1 -Test -Native -Gui -Package` on an ephemeral Windows runner with `LEAN81_NATIVE_FIXTURES=ephemeral-ci`. Required evidence includes successful .NET 4.5.1 compilation, policy/transaction/dependency/orchestration tests, disposable native Run/task/shortcut/process/service fixtures, x86 read-only resource ABI checks, GUI smoke at both sizes and browser report checks. Inspect the selective settings review, companion review and result screenshots, not only the smoke marker. Check individual selections, transitive dependency keeps, keep-list save/clear/re-include behavior and cancellation. Never toggle an installed service, existing task or unrelated process for tests.

Candidate package checks verify executable metadata, portable ZIP round-trip and SHA256 sums. Source, numeric assembly version, informational candidate version, package names and release notes must agree. Stable publication rejects an `-rc.N` version. Existing release tags are never overwritten.

## Windows 8.1 device validation still required

Use a backed-up Windows 8.1 installation under the same Windows account. Keep original recovery folders when replacing the EXE.

1. Launch in inspection mode; check small-screen layout, scan notes, missing counters and same-account elevation. Verify keyboard navigation reaches review and cancel. Check grouped feature switches, custom-preset state, focus indicators, screen-reader actions, high-contrast mode and 100% / 125% / 150% display scaling.
2. Choose a genuinely unused feature, review the actual installed items and tune. Check networking, audio, keyboard/touchpad, browser and required features. Inspect each result and its recovery group.
3. Restore that group and verify the feature and its original startup/running state. Test an independent configuration edit causes a recovery conflict instead of overwriting it.
4. Close one recognized companion with no unsaved work, first normally and then with the separately reviewed force option if needed. Verify the browser and foreground family remain running, then reopen the companion manually.
5. Check short observations on mains and battery, missing disk counters and closing the window during observation. Compare the same real browser workload across repeated runs; do not infer gains from synthetic values or one quiet eight-second window.
6. Reboot after an accepted tune, verify required features and startup changes, then reopen Lean81 and restore through Recovery. Record OS architecture, .NET version, package checksum, applied items and observed failures locally; keep private receipts out of public reports.

## Public release decision

Resolve device failures, rerun checks after fixes and review current GUI evidence. Decide whether to sign the executable; if left unsigned, keep that fact visible. Publish a preview while hardware coverage remains incomplete. A stable release needs an honest compatibility statement and hardware evidence; avoid any claim that it makes old hardware brand new.
