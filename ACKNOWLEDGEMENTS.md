# Acknowledgements

HashTool would not have been possible without the generous contributions of open-source projects, third-party libraries, and AI assistance during development.

All AI-generated content (code suggestions, translations, text) was carefully reviewed, tested, and verified by the author.

### AI Assistance
- **Gemini 3 Pro** (including Jules Agent) (by Google): Provided code suggestions, debugging help, error detection, and fixes.
- **Grok 4.1** (by xAI): Code suggestions, multi-language translations (30+ languages), and assistance in writing documentation/texts (e.g., Disclaimer, Privacy Policy).
- **Claude** (by Anthropic): The 1.2.0 overhaul — new hashing engine, checksum files, command-line tool, tests, CI/CD and release automation, bug fixes, and translations of the new texts.

Big thanks to these AIs for speeding up development.

### Third-Party Libraries
HashTool uses the following open-source libraries. Exact versions are listed on the app's *About* page and in the SBOM files (`*.cdx.json`) attached to every release.

| Library | Used for | License |
|---|---|---|
| [Avalonia UI](https://github.com/AvaloniaUI/Avalonia) (Avalonia, Desktop, Themes.Fluent, Headless) | Cross-platform user interface | MIT |
| [Inter](https://rsms.me/inter/) via Avalonia.Fonts.Inter | Default font | SIL OFL 1.1 (font), MIT (package) |
| [SkiaSharp](https://github.com/mono/SkiaSharp) and [HarfBuzzSharp](https://github.com/mono/SkiaSharp) | Rendering and text shaping (through Avalonia) | MIT |
| [Tmds.DBus.Protocol](https://github.com/tmds/Tmds.DBus) | Linux desktop integration (through Avalonia) | MIT |
| [Blake3.NET](https://github.com/xoofx/Blake3.NET) | BLAKE3 hashing | BSD 2-Clause |
| [System.IO.Hashing](https://github.com/dotnet/runtime) | xxHash and CRC32 | MIT |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | MVVM helpers | MIT |
| [Material.Icons.Avalonia](https://github.com/AvaloniaUtils/Material.Icons.Avalonia) | Icons | MIT |
| [Microsoft.Extensions.DependencyInjection / Logging](https://github.com/dotnet/runtime) | Composition and logging | MIT |
| [System.CommandLine](https://github.com/dotnet/command-line-api) | The `hashtool` command-line tool | MIT |
| [Velopack](https://github.com/velopack/velopack) | Installers and automatic updates | MIT |

Development and CI tools: [xUnit](https://github.com/xunit/xunit), [BenchmarkDotNet](https://github.com/dotnet/BenchmarkDotNet), [Meziantou.Analyzer](https://github.com/meziantou/Meziantou.Analyzer), [ReferenceTrimmer](https://github.com/dfederm/ReferenceTrimmer), [nuget-license](https://github.com/sensslen/nuget-license), [CycloneDX for .NET](https://github.com/CycloneDX/cyclonedx-dotnet), [zizmor](https://github.com/zizmorcore/zizmor), [actionlint](https://github.com/rhysd/actionlint), [gitleaks](https://github.com/gitleaks/gitleaks) and [OpenSSF Scorecard](https://github.com/ossf/scorecard).

Thank you to all the maintainers and contributors of these projects. Your work is greatly appreciated.

_© 2026 poli0981. All rights reserved._
