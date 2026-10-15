# Security Policy for HashTool

**Last updated: October 2026**

HashTool is a free, open-source desktop application built with C# (.NET 10) and Avalonia UI. It processes files locally, has no server components, and only goes online to check GitHub for updates.

### Supported Versions
Security fixes are released for the latest minor version only. Installed and portable copies update themselves, so please stay on the newest release.

| Version | Supported          |
| ------- | ------------------ |
| 1.2.x   | :white_check_mark: |
| < 1.2   | :x:                |

### How releases are protected
- Releases are built, tested and published by GitHub Actions from the `main` branch; every release includes `SHA256SUMS`, CycloneDX SBOMs and [build provenance attestations](https://docs.github.com/actions/security-for-github-actions/using-artifact-attestations) (`gh attestation verify <file> --repo poli0981/HashTool`).
- Every pull request is checked for vulnerable NuGet packages, licences, leaked secrets and unsafe workflow patterns, in addition to CodeQL and OpenSSF Scorecard.
- The apps are not code-signed yet, so Windows SmartScreen and macOS Gatekeeper may warn when they are first opened.

### Reporting a Vulnerability
If you discover a potential security vulnerability, please report it responsibly:

1. **Do not open a public issue** — this prevents malicious exploitation.
2. Use one of the following methods:
    - Open a **private vulnerability report** via GitHub's Security tab (preferred).
    - Or create a draft security advisory: [here](https://github.com/poli0981/HashTool/security/advisories/new)
    - If you can't use GitHub, email: [here](mailto:lopop05905@pm.me) (or just open a regular issue and mark it 
      private if possible).

Please include:
- Detailed description of the vulnerability
- Steps to reproduce
- Potential impact

### What to Expect
- I will acknowledge receipt within 48 hours (if possible).
- I'll investigate and respond with next steps (accept/decline, timeline for fix).
- If accepted, a fix will be released as soon as practical, and you'll be credited in the release notes (unless you prefer anonymity).
- If declined, I'll explain why.

Thank you for helping keep HashTool secure! 🛡️

© 2026 poli0981. All rights reserved.