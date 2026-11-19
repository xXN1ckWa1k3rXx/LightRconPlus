# Game-specific server settings

The desktop, setting validation, startup export and Lua variable registration share the game masks in `SettingsCatalog`. BFH is reported as `BFHL` by RCON. A server build may expose fewer commands; `admin.help` and command responses further restrict availability.

| Setting | BF3 | BF4 | BFH |
| --- | --- | --- | --- |
| `vars.serverName` | Yes | Yes | Yes |
| `vars.serverDescription` | Yes | Yes | Yes |
| `vars.serverMessage` | Yes | Yes | Yes |
| `vars.bannerUrl` | Yes | — | — |
| `vars.serverType` | — | Yes | Yes |
| `vars.maxPlayers` | Yes | Yes | Yes |
| `vars.maxSpectators` | — | Yes | Yes |
| `vars.commander` | — | Yes | — |
| `vars.hacker` | — | — | Yes |
| `vars.alwaysAllowSpectators` | — | Yes | Yes |
| `vars.autoBalance` | Yes | Yes | Yes |
| `reservedSlotsList.aggressiveJoin` | Yes | Yes | Yes |
| `vars.preset` | — | Yes | Yes |
| `vars.friendlyFire` | Yes | Yes | Yes |
| `vars.regenerateHealth` | Yes | Yes | Yes |
| `vars.soldierHealth` | Yes | Yes | Yes |
| `vars.bulletDamage` | Yes | Yes | Yes |
| `vars.playerRespawnTime` | Yes | Yes | Yes |
| `vars.playerManDownTime` | Yes | — | Yes |
| `vars.onlySquadLeaderSpawn` | Yes | Yes | Yes |
| `vars.forceReloadWholeMags` | — | Yes | Yes |
| `vars.unlockMode` | Yes | Yes | Yes |
| `vars.gunMasterWeaponsPreset` | Yes | Yes | — |
| `vars.vehicleSpawnAllowed` | Yes | Yes | Yes |
| `vars.vehicleSpawnDelay` | Yes | Yes | Yes |
| `vars.3pCam` | Yes | Yes | Yes |
| `vars.hud` | Yes | Yes | Yes |
| `vars.miniMap` | Yes | Yes | Yes |
| `vars.miniMapSpotting` | Yes | Yes | Yes |
| `vars.3dSpotting` | Yes | Yes | Yes |
| `vars.nameTag` | Yes | Yes | Yes |
| `vars.killCam` | Yes | Yes | Yes |
| `vars.crossHair` | Yes | Yes | Yes |
| `vars.hitIndicatorsEnabled` | — | Yes | Yes |
| `vars.gameModeCounter` | Yes | Yes | Yes |
| `vars.ctfRoundTimeModifier` | Yes | — | — |
| `vars.roundTimeLimit` | — | Yes | Yes |
| `vars.ticketBleedRate` | — | Yes | Yes |
| `vars.roundStartPlayerCount` | Yes | Yes | Yes |
| `vars.roundRestartPlayerCount` | Yes | Yes | Yes |
| `vars.roundLockdownCountdown` | Yes | Yes | Yes |
| `vars.roundWarmupTimeout` | Yes | Yes | Yes |
| `vars.idleTimeout` | Yes | Yes | Yes |
| `vars.idleBanRounds` | Yes | Yes | Yes |
| `vars.teamKillCountForKick` | Yes | Yes | Yes |
| `vars.teamKillKickForBan` | Yes | Yes | Yes |
| `vars.teamKillValueForKick` | Yes | Yes | Yes |
| `vars.teamKillValueIncrease` | Yes | Yes | Yes |
| `vars.teamKillValueDecreasePerSecond` | Yes | Yes | Yes |
| `vars.rankLimit` | Yes | — | — |
| `vars.premiumStatus` | Yes | — | — |
| `vars.mpExperience` | — | Yes | Yes |
| `vars.roundPlayersReadyBypassTimer` | — | Yes | — |
| `vars.roundPlayersReadyMinCount` | — | Yes | — |
| `vars.roundPlayersReadyPercent` | — | Yes | — |
| `vars.roundStartReadyPlayersNeeded` | — | — | Yes |

## Sources

Reviewed [AdKats/Procon-1](https://github.com/AdKats/Procon-1/tree/b0d3c070a2938aacc3f940facabc4b7c1bec5e39), especially `BF3Client`, `BF4Client`, `BFHLClient` and their startup queries/overrides. `BFHLClient.SendGetVarsCommander` and `SendSetVarsCommander` send `vars.hacker`. The v1.5.4.2 game-specific settings controls distinguish BF3 CTF/banner/rank fields, BF4 configuration, and Hardline configuration/gameplay (including the absent Gun Master control). The BF4 administration protocol documents `vars.crossHair`, so it remains available subject to the server capability check even though the older Procon control is disabled. BF4 ready-player controls and Hardline's `roundStartReadyPlayersNeeded` use separate names.

Map catalogs were compared against the same upstream revision's `BF3.def`, `BF4.def` and `BFHL.def`: all **215**, **315** and **240** map/mode pairs respectively match, with no missing or extra pairs. Available-map browsing uses only the detected game's catalog. Live rotations retain the selected server's game context.

Raw RCON remains available for server-specific extensions; known settings belonging to a different game are rejected before sending. Lua scripts can check a variable wrapper for nil before using it.
