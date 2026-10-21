# Reporting security issues

Use GitHub's private vulnerability reporting for this repository when available. If unavailable, open an issue asking for a private reporting channel without publishing exploit steps, private receipts, credentials or personal machine details.

Lean81 is a local privileged-settings tool. The normal app scans without elevation; mutations and private recovery storage require administrator mode. Elevation must keep the same Windows account. It never executes inspected commands or downloads runtime code.

Protection heuristics are conservative review aids, not a malware or digital-signature verifier. No untrusted plugin loading is supported. Expansion of the mutation surface requires explicit policy and recovery design.
