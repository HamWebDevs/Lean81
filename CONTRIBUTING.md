# Contributing

Start with a bug report, a small improvement or a proposal explaining the exact laptop problem. Include Windows version, x86/x64, Lean81 version, expected result and the visible error. Avoid private receipt files, startup commands, usernames, full paths and raw process names in public issues.

Use C# 5 and .NET Framework 4.5.1 APIs. Keep dependencies and idle work low. Read AGENTS.md. Every new system mutation needs a narrow policy, before-state capture, fresh-state preflight, post-write verification and conflict-aware restore. Never infer "unused" merely from an idle service or unfamiliar app name.

Run `./scripts/build.ps1 -Test -Gui -Package` on Windows and inspect the GUI screenshots. For native fixtures, use the ephemeral GitHub Actions runner; do not run mutation tests against installed services or a real user's tasks. Keep preview documentation explicit about untested Windows 8.1 behavior.

Build output, captures and recovery receipts do not belong in the source repository. CI screenshots are synthetic. Use HamWebDevs for public owner credits.
