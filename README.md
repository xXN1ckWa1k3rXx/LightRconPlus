# LightRcon+

A native Windows RCON manager for **Battlefield 3, Battlefield 4 and Battlefield Hardline**, inspired by Procon Frostbite. LightRcon+ aims to make everyday server administration lighter and faster through a focused WPF desktop interface and a separate console host.

**Pre-alpha.** Built with C# and .NET 10, without external NuGet packages, Electron or WebView. Lower resource use is a design goal; performance against Procon has not been benchmarked. BF3/BF4/BFHL support is implemented and tested with local protocol simulators; live-server compatibility still needs validation.

Maintainer: **xXN1ckWa1k3rXx**. Powered by **Codex (gpt-6.1-sol)**.

## vs. Procon Frostbite

> [!IMPORTANT] LightRcon+ still in experimental/under developer stage, We do not guarantee that many of the features will function as expected—this applies in particular to BF3/HL.  
If you are looking for stable and long-term server management or plugins supported, you should choose [Procon Frostbite](https://myrcon.net/files/file/29-procon-client/).  
LightRcon not support plugin yet.

| Area | LightRcon+ | Procon Frostbite v1.x |
| --- | --- | --- |
| Runtime | .NET 10 | .NET Framework 4.7 |
| UI Based | [WPF](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/) | WinForms |
| UI Design | More modern | OK |
| UX | Easy & Clean | Complex & Useful |
| Theme | Support Dark theme | Only default |
| etc. | - | - |

One of the biggest difference is LightRcon+ support logs sync.  
In Procon v1.x, Procon layer can't view the events and chat logs when they re-open the Procon; In LightRcon+, we tried to sync the chat and events logs to every LightRcon+ client. The Layer host will record the logs in local, and sending the logs to layer client.

The LightRcon+ also simplified some Procon operations, etc. Map list
> [!IMPORTANT] We do not currently know how the Map List performs in BF3/HL; if you encounter any issues, please create an issue.  

The map list now features groups to distinguish between DLC and the base map, and gamemodes w/ map

### Again, if you host a real server and want plguins supported (etc. admin command), you should go [Procon Frostbite](https://myrcon.net/files/file/29-procon-client/)

## Features

- Save, edit and manage multiple direct RCON or LightRcon+ Layer connections. The connected game is detected automatically.
- Manage players, teams, bans, reserved slots and spectator slots where supported. Dead players have a red background. Context menus offer team moves, kill, Battlelog profiles and BF4DB searches for BF4.
- Receive chat, joins, departures and kill events; send Say/Yell messages to everyone or a player. Known game message and weapon identifiers have readable display names.
- Browse game-specific maps/modes, edit and reorder rotations, save/load the server's `Admin/MapList.txt`, and double-click a rotation entry to switch immediately.
- Edit settings according to server capabilities. Boolean changes apply immediately; other fields use Apply. Export Startup.txt and MapList.txt separately.
- Monitor RCON and PunkBuster output. PunkBuster actions require server support; PunkBuster does not offer a kill command.
- Host a Layer with named accounts and per-account or inherited permissions, without sharing the upstream RCON password. Authorized Layer administrators can manage accounts remotely.
- English (default), Chinese and German; system, light and dark themes. Dates and numbers follow the local PC.
- Native framed desktop window and a headless console host. Plugins are **Work In Progress**; no DLL or Lua plugins are loaded.

## Requirements

Windows-only; current publish targets **x64**.

| Program | Framework-dependent runtime | Windows environment |
| --- | --- | --- |
| Desktop | .NET 10 Windows Desktop Runtime x64 | Desktop session; Windows Server requires Desktop Experience |
| Console host | .NET 10 Runtime x64 | Desktop Experience or Server Core; no WPF dependency |

The oldest intended server OS is **Windows Server 2019/Windows 10 1809.17763**, fully updated. **Suggest Windows Server 2022/Windows 10 21H2 (20348) and above** Desktop Windows must also be supported by .NET 10. The application's build-17763 minimum check is not a certification of every Windows edition. Actual Server 2019 and Server Core deployment has not yet been tested.

Install the appropriate runtime from [Microsoft's .NET 10 downloads](https://dotnet.microsoft.com/download/dotnet/10.0). A self-contained publish includes the runtime instead. Development requires the **.NET 10 SDK**; Visual Studio and the VS Code C# Dev Kit are optional. Git is needed for version control, not for running the application. Procon is not a runtime dependency.

A reachable BF3, BF4 or BFHL server with RCON enabled and its RCON password is required. Available commands, settings, maps and modes depend on the server version and installed content.

## Desktop setup

1. Build/publish as described below, then run `artifacts/desktop/LightRcon.Desktop.exe`.
2. Select **Create Connection** and enter a local instance name, `host:port` and password. IPv6 endpoints use `[address]:port`.
3. Leave the username empty for direct RCON. For a LightRcon+ Layer, enter the Layer username and its separate account password.
4. Enable automatic login if initial failures should retry every 20 seconds with no attempt limit. After a successful connection, unexpected disconnections retry automatically; manual disconnect stops retries.

Welcome retains Open / Edit / Remove for each instance. The sidebar expands after one second of hovering and collapses on pointer exit or loss of window focus. Settings → General changes language/theme; About contains author and license credits.

On unexpected disconnect, client preferences and drafts are saved and a connection-lost banner appears. Before reconnect synchronization, client state is saved again. Locally preferred server settings and pending rotation edits take priority over differing remote values; rejected settings are reported in Console. Keep this behavior in mind when multiple administrators edit the same server.

Map changes use server-side RCON `mapList` commands, not a file on the client PC. Saving persists the rotation using `mapList.save`; explicitly loading MapList.txt uses `mapList.load` and replaces the live rotation. An immediate switch sets the selected index before running the next round. Layer Startup exports replace `admin.password` with `*MUST REPLACE TO YOUR RCON PASSWORD*`.

## Headless deployment

The console host creates a direct connection and the first fully authorized Layer administrator **without desktop setup**. RCON and Layer administrator passwords are separate credentials; the Layer administrator password must contain at least 12 characters. Prefer environment-variable arguments so secrets are not included in the process command line.

Example PowerShell bootstrap (replace the example values locally):

```powershell
$env:LIGHTRCON_RCON_PASSWORD = 'replace-with-rcon-password'
$env:LIGHTRCON_ADMIN_PASSWORD = 'replace-with-a-separate-admin-password'

.\artifacts\host\LightRcon.Host.exe `
  --data-dir 'D:\LightRconData' `
  --connect '127.0.0.1:47200' `
  --name 'Main server' `
  --rcon-password-env LIGHTRCON_RCON_PASSWORD `
  --layer-bind '127.0.0.1' `
  --layer-port 47300 `
  --admin-user admin `
  --admin-password-env LIGHTRCON_ADMIN_PASSWORD `
  --configure-only

Remove-Item Env:LIGHTRCON_RCON_PASSWORD, Env:LIGHTRCON_ADMIN_PASSWORD
.\artifacts\host\LightRcon.Host.exe --data-dir 'D:\LightRconData'
```

`--configure-only` saves without opening connections. Omit it to configure and start immediately. Repeating bootstrap for the same direct endpoint updates its profile and named administrator. The administrator receives all permissions; use a desktop Layer connection to create additional accounts.

For remote clients, choose the listener interface with `--layer-bind` and configure the firewall yourself. New CLI profiles bind to `127.0.0.1` by default. A desktop-created listener currently defaults to `0.0.0.0`; check its bind address before enabling it.

```powershell
.\artifacts\host\LightRcon.Host.exe --help
.\artifacts\host\LightRcon.Host.exe --data-dir 'D:\LightRconData' --list
.\artifacts\host\LightRcon.Host.exe --data-dir 'D:\LightRconData' --server '<saved-profile-guid>'
```

Other bootstrap options include `--name`, `--rcon-password` and `--admin-password`. The direct password flags expose values in process arguments; prefer their `-env` alternatives. Omitting `--server` runs all saved profiles. Ctrl+C saves and stops sessions. This is a console application, not an installed Windows Service; unattended execution can use Windows Task Scheduler under the Windows account that owns the saved data.

## Layer transport and local data

LightRcon+'s Layer protocol **does not interoperate with Procon Layer clients**. It uses TLS 1.2, username/password login, PBKDF2-SHA256 hashes with random salts and server-side command permissions. Layer clients cannot retrieve the upstream RCON password.

**Current limitation:** Layer clients encrypt transport but do not validate the listener certificate's identity. This does not protect against an active attacker impersonating a listener. Use a trusted network or an authenticated VPN/tunnel for remote deployment. Direct Frostbite RCON is also not a TLS transport. Web administration is not implemented.

The default data directory is `%LOCALAPPDATA%\LightRconPlus`; `--data-dir PATH` selects another. Only one desktop/host process may use a directory within a Windows session. Use independent directories for separate installations; avoid sharing a directory across concurrent Windows sessions.

| Location | Contents |
| --- | --- |
| `profiles.json` | Connections, UI preferences, locally preferred settings/maps, drafts and Layer accounts |
| `servers/<id>/activity-yyyy-MM-dd.jsonl` | Chat/event history when Layer hosting is enabled |
| `servers/<id>/` | Per-server Layer certificate material and related state |

Connection passwords and Layer private keys use **Windows DPAPI under the current user**. Moving to another account/machine requires re-entering secrets and regenerating certificates. Protect the whole data directory: it contains player identifiers, IP addresses and logs. Never commit it.

Direct sessions without Layer hosting keep chat in memory and discard it when the process closes. Enabling hosting persists chat/events even with no downstream clients. New Layer clients receive saved history followed by live events. The UI retains 5,000 entries; persistent files are not automatically pruned. Disabling a Layer does not delete existing files.

## Build, test and publish

From the repository root on Windows:

```powershell
dotnet build LightRcon.slnx -c Release
dotnet run --project src/LightRcon.Desktop
dotnet run --project tests/LightRcon.Tests -c Release
dotnet run --project tests/LightRcon.Desktop.Tests -c Release
.\tools\publish.ps1
```

The test projects are executable regression harnesses; use `dotnet run`. Desktop checks open temporary WPF windows and need an interactive desktop. Tests use isolated temporary data and loopback mocks, with no real game-server credentials. Reconnect checks intentionally take at least 20 seconds.

The publish script creates framework-dependent single-file win-x64 programs in `artifacts/desktop/` and `artifacts/host/`, together with README, license and notices. Use `./tools/publish.ps1 -SelfContained` to include .NET; output will be larger. Stop the application before publishing over an installed copy. Intermediates and local application data are excluded from Git.

See [validation notes](docs/VALIDATION.md) for tested scope and remaining gaps. Mock success is not live-server or production acceptance. Unsupported settings are disabled. BF4 ticket estimates are reference values; BF3/BFHL do not reuse them. Live team scores come from `serverInfo`. Unknown player fields display as unavailable; Play Time is the client's observed duration. Map rotation edits are not atomic server transactions and competing administrators can cause conflicts.

## Source layout

```text
src/LightRcon.Core/       Frostbite transport, sessions, game catalogs, storage and Layer
src/LightRcon.Desktop/    WPF interface, themes and localized presentation
src/LightRcon.Host/       Headless host and CLI entry point
tests/                   Protocol/host and WPF regression harnesses
tools/                   Publishing and game-data import scripts
docs/                    Validation notes
LICENSE                  GNU GPL version 3
THIRD-PARTY-NOTICES.md    Procon provenance and third-party acknowledgments
```

Embedded catalogs include converted Procon BF3/BF4/BFHL definitions and selected labels. `tools/import-game-data.ps1` regenerates BF3/BFHL catalogs from locally supplied definitions and English localization; see [third-party notices](THIRD-PARTY-NOTICES.md). Development reference downloads and scratch output belong under ignored `.local/`, outside distributable source.

## AI use and maintenance

**The initial version of this project was created entirely by AI using Codex (gpt-6.1-sol)**, including its initial implementation, interface, tests and documentation, from requirements and iterative feedback supplied by xXN1ckWa1k3rXx. This is an AI-origin disclosure, not a claim of independent human authorship or clean-room development. AI-generated work can contain mistakes; passing current checks does not establish complete compatibility or security.

**xXN1ckWa1k3rXx will continue maintaining this project and reviewing pull requests and issues.** Contributions and reproducible reports are welcome. Include game/server versions and sanitized reproduction details; omit passwords and private player data. See [CONTRIBUTING.md](CONTRIBUTING.md).

## License and acknowledgments

Copyright (C) 2026 xXN1ckWa1k3rXx and contributors. Released under the **GNU General Public License, version 3 (GPL-3.0-only)**, **without any warranty**. Binary redistribution must comply with GPLv3, including applicable corresponding-source obligations. Read [LICENSE](LICENSE).

Inspired by Procon Frostbite; includes data derived from its GPL-covered definitions/localization. Credit belongs to Geoffrey 'Phogue' Green, Myrcon and Procon contributors. Original rights/notices remain applicable; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Battlefield and associated marks/game content belong to Electronic Arts, DICE and their respective rights holders. Battlefield Hardline was developed by Visceral Games. This independent community project is not affiliated with or endorsed by EA, DICE, Visceral Games or the Procon team. GPL licensing does not grant rights to EA's trademarks or proprietary game content.
