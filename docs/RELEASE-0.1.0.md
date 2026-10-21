Lean81's first native Windows preview: a small desktop workbench for older laptops with limited CPU and RAM.

Download **Lean81-0.1.0-portable.zip**, extract it, and open **Lean81.exe**. The standalone EXE is also attached. No Python, bundled browser, installer or permanent service is required.

Included:

- Startup-entry review, third-party task controls and usage-based optional-service choices.
- Installed-app inventory and native Windows tool links.
- On-demand CPU/process capture, a CPU graph and ReclaimLab trace export. Process names are omitted from exports by default.
- Explicit review, protected recovery receipts, fresh-state checks and exact-state recovery.
- Synthetic demo: `Lean81.exe --demo`.

This is an **unsigned preview targeting Windows 8.1 and .NET Framework 4.5.1**. Physical Windows 8.1 validation is pending. Automated testing uses Windows Server 2022: 53 core policy/transaction tests, six native fixture/capture/storage checks, a native-suite completion check, eight rendered pages, a cancelled review dialog and a small-window layout check. Native tests only change disposable Run/task fixtures; they never toggle an installed service.

Start by scanning. Keep uncertain features checked in Usage profile, and read each selected item's tradeoff. No changes are applied automatically, and no performance improvement is promised without measurements on your laptop. Keep `%ProgramData%\Lean81` for recovery; its private receipt JSON can contain startup commands and should not be shared.

Security, networking, core OS and update services stay outside the service allowlist. See the included COMPATIBILITY.md and RECOVERY.md for scope, limitations and partial-failure handling.

Portable ZIP, source ZIP and SHA256 checksums are attached. Public owner: **HamWebDevs**. MIT licensed.
