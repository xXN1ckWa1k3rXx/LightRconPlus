# Validation notes

Date: 2026-11-18 (Asia/Shanghai). Development machine: Windows x64, .NET SDK 10.0.404. Release compilation treats warnings as errors.

## Automated results

- Release solution build: zero warnings/errors.
- Protocol/host regression harness: **147 checks passed**.
- WPF regression harness: **112 checks passed**.
- Both harnesses exited with code 0.
- No external NuGet packages.
- Published win-x64 desktop/host single-file packages successfully. Smoke-tested the published host's help, configuration-only bootstrap, profile listing, rejection of a short administrator password without changing saved state, and absence of plaintext passwords in saved profiles.

Tests use isolated temporary profiles and loopback Frostbite simulators. They never connect to a production server. The simulator encodes responses independently of the application's packet writer; it models BF3, BF4 and BFHL but cannot prove real-server compatibility.

## Protocol, games and host coverage

- Binary framing, UTF-8, split writes, correct origin/response flags, malformed input rejection, hashed login, event acknowledgments and timeout/retry behavior.
- Game detection from `version` for direct and Layer connections. BF3/BFHL catalogs are separate from BF4. Changing an endpoint's game discards incompatible saved settings/maps before reconciliation.
- Embedded catalogs: BF3 has 29 maps, 215 faction/mode combinations and 91 weapon IDs; BF4 has 33 maps, 315 faction/mode combinations and 369 weapon IDs; BFHL has 27 maps, 240 faction/mode combinations and 183 weapon IDs. These are reference-derived labels, not a guarantee that all combinations exist on a particular server.
- Dynamic player fields, decimal team-score rounding, variable score blocks, target score/uptime separation, invalid/truncated score handling, map/mode-specific factions and unknown fallbacks.
- Initial `player.isAlive` seeding, kill/spawn state retained across polling, historical `player.spawn` handling, and Layer forwarding. Unknown state is not treated as dead.
- Game-specific map labels, rotation edits/save/load, rollback after rejection, serialization of rapid edits, offline preferred rotation and exact-index immediate switching. Mock runtime/disk lists are separate; disk-load failures do not overwrite the simulated source file.
- Setting read/write/reconciliation, presets and lock flags, capability detection, BF3's nine Gun Master presets, and BFHL's hacker setting. Missing BF3 spectator commands do not prevent connecting.
- Ban/list pagination and PunkBuster player enrichment.
- EN/ZH/DE message localization, exact `ID_CHAT_ATTACK/DEFEND` alias, structured/legacy kill messages, unknown-ID preservation, raw journal serialization and Layer history replay.
- Memory-only direct chat; persistent Layer chat without clients; history beyond the 5,000-entry display limit; appends during replay and monotonic history IDs.
- TLS/account authentication, invalid-password rejection, permission checks, blocked upstream password reads, blocked arbitrary/chained PunkBuster execution, account-hash non-disclosure and invalidation after account edits.
- Headless parser rejects missing, conflicting, unknown and duplicate options. Bootstrap persists DPAPI-protected RCON secrets and a PBKDF2 administrator with all permissions; repeated configuration creates no duplicate endpoint/account.
- Battlelog game selection and URL escaping; BF4DB applicability/escaping.
- Failed login retry after 20 seconds, unexpected disconnect persistence, reconnect restoration and client drafts.

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

Local verification output is ignored under `.local/validation/`. Normal builds may create ignored bin/obj directories; publishing writes to ignored artifacts/.

## Remaining validation

No real BF3, BF4 R63 or BFHL server was contacted. Actual Windows Server 2019 Desktop Experience / Server Core deployment, real PunkBuster behavior, full-server event load, every map/mode/weapon label, multiple-administrator conflicts and all DPI/localization layouts still need testing.

Layer clients currently do not validate the listener certificate's identity. Account permission tests do not constitute an independent security audit or comprehensive rate-limit/load test. Use a trusted network or an authenticated tunnel.

RCON map edits and set-next/run commands are not atomic transactions across independent administrators. Tests verify local serialization and stale-list checks, not global transaction safety.
