# Contributing

xXN1ckWa1k3rXx maintains LightRcon+ and reviews pull requests and issues. The initial project was generated entirely with Codex; all contributions need careful review regardless of whether a person or an AI wrote them.

Use English for code identifiers, documentation, issues and pull request descriptions. Chinese and German belong in the localization catalog. Keep EN/ZH/DE keys and formatting placeholders consistent.

## Development

Install the .NET 10 SDK on Windows, then run:

```powershell
dotnet build LightRcon.slnx -c Release
dotnet run --project tests/LightRcon.Tests -c Release
dotnet run --project tests/LightRcon.Desktop.Tests -c Release
```

Desktop checks require an interactive Windows desktop. Tests use temporary profiles and local mock servers. Game-specific behavior belongs behind the detected game/capabilities; do not assume BF4 commands or ticket estimates work for BF3/BFHL.

Keep dependencies small and explain new runtime requirements. Add meaningful regression coverage for protocol changes and bugs. Describe the resulting behavior, checks performed and remaining live-server gaps. Preserve third-party copyright/source attribution and disclose substantial AI assistance in the pull request.

## Bug reports

Include the game, server build, Windows/.NET version, direct/Layer mode and reproduction steps. Sanitize logs: omit passwords, hashes, certificates and private player data. Identify mock-only reproductions separately from live observations.

## License

Contributions must be compatible with GPLv3. By submitting original contributions, you agree to license them under GPL-3.0-only. Identify sources/licenses for third-party material and retain upstream notices.
