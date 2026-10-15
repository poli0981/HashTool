# Privacy Policy for HashTool

**Last updated: October 2026**

HashTool is a free, open-source desktop application distributed under the MIT License (see LICENSE file).

### Data Collection and Usage
HashTool does **not** collect, store, or transmit any personal data, usage statistics, or telemetry.

- No user information is gathered.
- No analytics or tracking of any kind.
- The application processes only the files you explicitly select for hashing, and all operations occur locally on your device.

### Online Features
HashTool only talks to the network to look for updates:

- **Automatic update check** (on by default, can be turned off in *Settings → Updates*): at most once a day, when the app starts, it asks the public GitHub Releases API whether a newer version exists.
- **Check Now** on the *Update* page does the same on request.
- An update is downloaded from GitHub only when you choose to install it.

These requests contain nothing about you or your files — GitHub sees the usual information of any web request (such as your IP address).
For details about GitHub's data handling, please refer to [GitHub's Privacy Policy](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement).

### Data Stored on Your Device
HashTool keeps its settings and logs in a local folder (`%LOCALAPPDATA%\HashTool` on Windows, `~/Library/Application Support/HashTool` on macOS, `~/.local/share/HashTool` on Linux):

- `settings/config.json` — your preferences.
- `log/` — diagnostic logs, kept for 14 days. They may contain names of files you hashed; your user folder name is masked.

Nothing in this folder is ever sent anywhere. *Settings → Configuration → Open Data Folder* shows it; deleting it resets the app.
If you turn on the Explorer context menu (Windows), HashTool adds entries under your user's registry (`HKEY_CURRENT_USER\Software\Classes`); turning the option off or uninstalling removes them.

### Translations
Any non-English translations (if provided) are generated with AI assistance and provided as-is. They do not involve sending your data anywhere.

### Changes to This Policy
The author may update this privacy policy at any time. Continued use of HashTool after changes constitutes acceptance of the updated policy.

### Contact
Questions about this policy? Open an issue on the GitHub repository.

© 2026 poli0981. All rights reserved (except the rights granted under the MIT License).