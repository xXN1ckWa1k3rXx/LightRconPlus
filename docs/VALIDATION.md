# Validation notes

Date: 2026-11-23 (Asia/Shanghai). Development machine: Windows x64, .NET SDK 10.0.404. Release compilation treats warnings as errors.

## Automated results

- Release solution build: zero warnings/errors.
- Protocol/host regression harness with Procon integration enabled: **351 checks passed**.
- WPF regression harness with Procon integration enabled: **213 checks passed**.
- Both harnesses exited with code 0.
- Plugin tests require restoring MoonSharp and Roslyn. Set `LIGHTRCON_TEST_PROCON_ZIP` to a local Procon v1.5.4.2 release archive to include upstream integration checks.
- Published win-x64 desktop/host single-file packages successfully. Smoke-tested the published host's help, configuration-only bootstrap, profile listing, rejection of a short administrator password without changing saved state, and absence of plaintext passwords in saved profiles.
- Published single-file host also loaded native C# source, Lua and Procon source plugins and delivered their commands to the simulator. Dependency licenses are included in each package; API references/assemblies, examples, screenshots and development documents remain in the repository.

## Plugin and reconnection coverage

- Pre-alpha 1.2 uses lower-allocation profile snapshots and pooled UTF-8 Layer writes. Concurrent large Unicode frames and cancellation followed by buffer reuse have regression coverage. Native plugins omit empty timer work; Lua timer behavior and native lifecycle callbacks remain covered by the integration suite. Both runtime configs select workstation/concurrent GC with ConserveMemory=9. Two fresh-process memory comparisons and their limits are recorded in [MEMORY.md](MEMORY.md).

- Follow-up coverage verifies immediate host/Layer rotation pushes, five-second external-rotation discovery, atomic map writes, stale/live-rotation conflict checks and Maps permissions. WPF verifies before/after insertion positions, preview drawing/cleanup, copyable player/country/identity fields, the server action row, player-name/GUID filtering and chat-only filtering. BF4 tests cover all four faction overrides, Default export omission, separate RCON arguments and Layer permissions. Chinese Second Assault is 再次进击.
- Lua error tests exercise nested callback tracebacks with caller names/line locations, syntax errors, native API failures caught by `pcall`, independent failing/healthy timers, absolute-path redaction in print/log/errors, a runaway loop inside `pcall` and later event/RCON recovery. This provides failure isolation and diagnostics, not interactive breakpoint debugging.

- Layer batch state/settings snapshots avoid upstream version/help/player/life-state/map queries during client login. Setting edits still reach the host RCON connection and update the host's desired configuration. A 6,000-entry archive with 1,500-character messages verifies responses arrive before replay completes, while every historical sequence remains ordered. Snapshot support is negotiated through an additive protocol 1 welcome field, with legacy paged commands retained as fallback.
- PRoCon console colors (`^0`–`^9`), bold/italic/normal font codes, literal carets, unknown sequences, selection/copy and Layer console history are checked. Layer details hide both the File label and path. Country flags load from bundled images with unknown-code fallback; the Players template is checked after IP enrichment. Chat tests cover reading older messages, new arrivals, tab changes and repeated viewport resizing.

- Shared Procon v1.5.4.2 runtime: source compilation, DLL loading, parent/nested includes, original upstream CSpambot and CInGameAdmin, real instance identity in `OnPluginLoaded`, metadata, variable groups/types/read-only/string arrays, inter-plugin calls, Lua calls and configuration restoration after connection loss.
- A generated .NET Framework dependency renamed to `.cs` is staged byte-for-byte and loaded by a plugin constructor, while upstream source compilation skips the binary. Invalid Procon source returns its actual failure while logs remain open; subsequent metadata/settings reads return cached failure state without rebuilding. Disabled and failed Procon plugins retain the localized warning. Cancellation stops plugin restoration and permits closing during startup.
- The supplied, unmodified BF4DB 2.0.16 package was checked separately against a loopback simulator. Omitting `BF4DB_API.cs` returned the actual missing-file error in 3.41 seconds. Restoring the dependency and explicitly importing again loaded metadata and 11 settings in 2.51 seconds. Both original files and staged helper bytes were verified unchanged. The plugin was left disabled; no external BF4DB API/key validation was performed.
- Native C# API version declaration and rejection of unsupported API versions.
- Lua metadata and local modules, structured player/kill events, targeted and squad messages, typed settings, validation, lifecycle callbacks, serialized timers, JSON, isolated persistent storage, HTTP request headers and response status/body and instruction limits.
- WPF Details and Plugin Settings pages use actual runtime metadata and variables. Focus loss changes runtime and persisted configuration; expanded multiline editors save on Apply. Disabled plugins remain selectable. EN/ZH/DE warning bindings and preservation of unsaved plugin settings during language changes are checked.
- Manual connection buttons are enabled while retrying; a click interrupts the 20-second countdown and reconnects immediately. Enabled Lua/Procon plugins and saved values are restored. Repeated requests do not create another session runner.
- The console Host uses a five-second retry interval. Checks verify the actual retry timing, F3 handling while waiting and while connected, and ignoring unrelated keys through the keyboard handler's input callback. Redirected console input does not invoke `Console.ReadKey`. Host status includes instance IDs/endpoints; password rejection, refused TCP connections and post-authentication synchronization errors retain their stage and command/socket details. The desktop retains its twenty-second retry interval.
- Layer authentication checks reload a stored profile and its administrator/user hashes unchanged, authenticate both accounts, distinguish missing/disabled accounts, password mismatch and missing View permission in local diagnostics, preserve generic client errors and reject accounts from another data directory. Console diagnostics are checked for absence of passwords and hashes. An isolated executable upgrade test creates accounts with the previous Host, starts the new Host against the same data while BF4DB compiles, and authenticates those accounts with both current and previous client assemblies.
- Plugin libraries are discovered at RCON startup, successful Layer sign-in and explicit Refresh list requests. Tests leave new/updated files untouched across RCON polling intervals until a sign-in/refresh occurs. Flat C#/DLL/Lua discovery, source preference, exclusion of binary `.cs`/database/API dependencies, batched Procon compilation, rule-file staging, include-only wrappers, isolated package failures, configuration restoration and Layer broadcasts are checked. No timer or filesystem watcher scans the plugin library. Existing Layer clients continue using the same `plugin.list` operation for Refresh list.
- Procon cache checks distinguish actual compilation (`Done`) from upstream `Using Cache`. A supplied adjacent DLL is loaded byte-for-byte even when its source includes are missing. Source-only plugins retain generated DLLs and cache entries across reconnect/shared-runtime rebuilds. Source/include/partial changes invalidate individual entries; replaced external DLLs load directly, and older unchanged library DLLs cannot overwrite newly compiled output.
- Cache invalidation checks include Unicode source/metadata, replacing a supplied DLL, updating its source afterwards, preserving the newly compiled output over the older library DLL and recreating a missing generated DLL. A library `PluginCache.xml` does not replace the per-server runtime cache. Worker stdin/stdout use UTF-8.
- Four unmodified plugins (BF4DB 2.0.16 and upstream CBasicInGameInfo, CInGameAdmin, CSpambot) were auto-discovered and compiled together against the simulator in 7.42 seconds. BF4DB exposed 11 settings. All remained disabled; the test did not call the external BF4DB API.
- Chat uses a read-only rich-text editor: player, killer and victim team colors, localized weapon labels and raw-ID tooltips are preserved with selectable text. WPF checks cover live chat colors, dark-theme updates, selection, scrolling, clear and logical line limits. RCON Console remains a plain selectable editor; Plugin console renders PRoCon colors and font styles.
- WPF screenshots of both pages and the enabled Procon banner were rendered and inspected in `artifacts/validation/`.
- Layer clients list only upstream plugins, read upstream metadata/settings and modify settings on the host with Settings permission. LayerAdmin is required for activation. Tests cover read-only/typed validation, rejection of arbitrary host paths, persisted host values, list/state broadcasts to multiple clients and reconnect without duplicate runtimes.
- Plugin activation uses one prominent blue action beside the selected plugin name. The compatibility warning appears only in the selected Procon plugin's content area, including disabled and failed plugins. Tab titles retain the blue accent under hover. The listener shows exactly one blue Enable/Disable action and locks its endpoint while listening.
- Selectable Chat and Console editors follow the bottom by default and preserve manual scrolling/selection. Chat defaults to 450 lines, configurable from 100 to 800; Clear chat hides current entries without erasing files. Clear all logs deletes persisted logs without deleting profiles/plugins. Direct connections export server/launch/connection-grouped UTF-8 chat and event logs. Manual ban fields have associated titles; seconds, minutes, hours, days, weeks and 30-day months convert to protocol seconds with overflow rejection. Round-based and permanent bans do not use time conversion.

Tests use isolated temporary profiles and loopback Frostbite simulators. They never connect to a production server. The simulator encodes responses independently of the application's packet writer; it models BF3, BF4 and BFHL but cannot prove real-server compatibility.

## Protocol, games and host coverage

- Binary framing, UTF-8, split writes, correct origin/response flags, malformed input rejection, hashed login, event acknowledgments and timeout/retry behavior.
- Game detection from `version` for direct and Layer connections. BF3/BFHL catalogs are separate from BF4. Changing an endpoint's game discards incompatible saved settings/maps before reconciliation.
- Embedded catalogs: BF3 has 29 maps, 215 faction/mode combinations and 91 weapon IDs; BF4 has 33 maps, 315 faction/mode combinations and 369 weapon IDs; BFHL has 27 maps, 240 faction/mode combinations and 183 weapon IDs. These are reference-derived labels, not a guarantee that all combinations exist on a particular server.
- Dynamic player fields, decimal team-score rounding, variable score blocks, target score/uptime separation, invalid/truncated score handling, map/mode-specific factions and unknown fallbacks.
- Initial `player.isAlive` seeding, kill/spawn state retained across polling, historical `player.spawn` handling, and Layer forwarding. Unknown state is not treated as dead.
- Game-specific map labels, rotation edits/save/load, rollback after rejection, serialization of rapid edits, offline preferred rotation and exact-index immediate switching. Mock runtime/disk lists are separate; disk-load failures do not overwrite the simulated source file.
- Setting read/write/reconciliation, presets and lock flags, capability detection, BF3's nine Gun Master presets, and BFHL's hacker setting. Settings controls, Lua variables, reads, writes and startup exports share explicit game ownership; cross-game commands are rejected even if advertised by the simulator. Layer retains the same separation. Missing BF3 spectator commands do not prevent connecting.
- Applying a setting replaces its older saved draft with the submitted editor value before synchronization, preventing stale drafts from restoring an earlier percentage after a successful write.
- Ban/list pagination and PunkBuster player enrichment.
- EN/ZH/DE message localization, exact `ID_CHAT_ATTACK/DEFEND` alias, structured/legacy kill messages, unknown-ID preservation, raw journal serialization and Layer history replay.
- Direct chat exports and persistent Layer chat without clients; replay history beyond the visible editor limit; appends during replay and monotonic history IDs.
- TLS/account authentication, invalid-password rejection, permission checks, blocked upstream password reads, blocked arbitrary/chained PunkBuster execution, account-hash non-disclosure and invalidation after account edits.
- Headless parser rejects missing, conflicting, unknown and duplicate options. Bootstrap persists DPAPI-protected RCON secrets and a PBKDF2 administrator with all permissions; repeated configuration creates no duplicate endpoint/account.
- Battlelog game selection and URL escaping; BF4DB applicability/escaping.
- Desktop failed login retry after 20 seconds, console Host retry after 5 seconds, unexpected disconnect persistence, reconnect restoration and client drafts.

## WPF coverage

The STA harness creates temporary windows in an interactive desktop session.

- Repeated close and connected/connecting shutdown, embedded connection editor, cancellation/inline errors and normal Windows frame.
- English default, saved language/theme restoration, complete EN/ZH/DE entries and matching format placeholders, OS date/number culture preservation.
- Sidebar stays collapsed during brief hover, cancels delayed expansion on exit, expands after one second with animation, and collapses on exit/deactivation even with keyboard focus inside. Repeated cycles do not leave it expanded.
- Instance/server/address Welcome naming and consistent Layer server-name prefixes.
- Team grouping, live ticket display, blank-area deselection, right-click selection, faction-named move/kill commands and profile menus. Offline actions and non-BF4 BF4DB links are disabled.
- Red death background on both row/cells while selected in dark mode, light palette updates and restoration after spawn.
- Action labels and minute/hour/day/week/month conversion, overflow rejection, Server seconds vs PunkBuster minutes, permanent ban independence from duration.
- Top-left setting inputs, scroll-following outline, immediate Startup Config, checkboxes with immediate sync, preserved unsent edits, numeric steppers and game-specific choices.
- Percentage slider edits the cvar and Apply sends the percentage; BF4 reference estimates preserve fractions, unknown modes remain editable, and game changes do not silently reuse BF4 ticket bases.
- Themed map expanders, rapid additions with correct mode/round values, current-map double-click dispatch, and no switch from empty-space/right double-click.
- Layer viewer/admin tab visibility, hidden upstream listener controls and remote account loading.
- BF3/BFHL desktop map browsers and profile menu availability; BF3's nine Gun Master choices.

## Reproduction

```powershell
dotnet build LightRcon.slnx -c Release
dotnet run --project tests/LightRcon.Tests -c Release
dotnet run --project tests/LightRcon.Desktop.Tests -c Release
.\tools\publish.ps1
```

Local verification output is ignored under `.local/validation/`. Builds and intermediate outputs are centralized under ignored `artifacts/build/`; release packages go to the sibling `<checkout> release` directory, or the supplied `-OutputDirectory`.

To run the optional upstream checks, set `LIGHTRCON_TEST_PROCON_ZIP` to the original `Procon-v1.5.4.2.zip` release archive before running either harness. For the single-file smoke test, run `dotnet run --project tests/LightRcon.Tests -c Release -- --published-host "../LightRcon+ release/LightRconPlus-Host-x86_64_win (with dotnet)/LightRcon.Host.exe"` after publishing. Set `LIGHTRCON_UI_CAPTURE_DIR` to a local output folder to capture the WPF pages.

## Remaining validation

Administration regression coverage includes Layer Say/Yell attribution in game, persisted history and other clients; duplicate server echo suppression; plugin-only Say/Yell display filtering, always-visible manual host/Layer chat and its saved per-connection preference; IP enrichment in Layer player snapshots; offline Geo lookup with unknown/private address handling; Procon account permission updates, inheritance and revocation; minimum-width Players layout; and the confirmed `mapList.endRound <winner>` action with Maps permission and two/four-team validation. Run `--administration` on the Core harness for the focused checks (set `LIGHTRCON_TEST_PROCON_ZIP` to include Procon).

Procon localization checks exercise the original language callbacks and formatted team matching. The unmodified `CInGameAdmin` plugin accepts `!nuke US Army` and `!nuke Chinese Army` on the local RCON simulator; countdowns use readable names and delayed kill commands target only the matching team. The uploaded `Untitled-1.cs` matches the upstream `Plugins/CInGameAdmin.inc` reference. These checks do not run nuke commands on a real server.

Plugin-console checks cover selectable WPF output, filtering out unrelated RCON logs, recent/live host output delivered to Layer viewers, entry-ID deduplication after reconnect, and published Host stdout from native C#, Lua `print`/`log.info` and Procon plugin-console calls.

Geo uses a historical offline database whose original vintage is unspecified; it does not verify present-day address ownership. This product includes GeoLite data created by MaxMind, available from http://maxmind.com/.

No real BF3, BF4 R63 or BFHL server was contacted. Actual Windows Server 2019 Desktop Experience / Server Core deployment, real PunkBuster behavior, full-server event load, every map/mode/weapon label, multiple-administrator conflicts and all DPI/localization layouts still need testing.

Layer clients currently do not validate the listener certificate's identity. Account permission tests do not constitute an independent security audit or comprehensive rate-limit/load test. Use a trusted network or an authenticated tunnel.

RCON map edits and set-next/run commands are not atomic transactions across independent administrators. Tests verify local serialization and stale-list checks, not global transaction safety.

CSpambot regression checks use seven announcement lines and verify two complete rotations, unchanged array encoding on focus loss, edits to another plugin, and no immediate first-line repeat with a 120-second interval.
