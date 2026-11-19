# vars

Server variable wrappers. Values and support differ between BF3, BF4 and BFHL. The server is authoritative. Digit-prefixed names require bracket syntax, for example `vars["3pCam"]()`. Settings changes are RCON commands and are separate from the plugin settings namespace.

## serverName

```lua
vars.serverName([value])
```

Server name. Forwards to `vars.serverName`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | string / number / boolean | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## serverDescription

```lua
vars.serverDescription([value])
```

Description. Forwards to `vars.serverDescription`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | string / number / boolean | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## serverMessage

```lua
vars.serverMessage([value])
```

Join Message. Forwards to `vars.serverMessage`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | string / number / boolean | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## serverType

```lua
vars.serverType([value])
```

Server type. Forwards to `vars.serverType`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | string / number / boolean | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## maxPlayers

```lua
vars.maxPlayers([value])
```

Maximum players. Forwards to `vars.maxPlayers`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | number | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## maxSpectators

```lua
vars.maxSpectators([value])
```

Maximum spectators. Forwards to `vars.maxSpectators`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | number | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## commander

```lua
vars.commander([value])
```

Commander. Forwards to `vars.commander`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | boolean | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## hacker

```lua
vars.hacker([value])
```

Hacker. Forwards to `vars.hacker`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | boolean | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## alwaysAllowSpectators

```lua
vars.alwaysAllowSpectators([value])
```

Always allow spectators. Forwards to `vars.alwaysAllowSpectators`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | boolean | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## autoBalance

```lua
vars.autoBalance([value])
```

Auto balance. Forwards to `vars.autoBalance`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | boolean | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## preset

```lua
vars.preset([preset, locked])
```

Game preset (server side). Forwards to `vars.preset`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| preset | string | Omit to read; otherwise NORMAL, CLASSIC, HARDCORE, INFANTRY or CUSTOM, subject to game support. |
| locked | boolean | Required when setting a preset; controls the preset lock. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## friendlyFire

```lua
vars.friendlyFire([value])
```

Friendly fire. Forwards to `vars.friendlyFire`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | boolean | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## regenerateHealth

```lua
vars.regenerateHealth([value])
```

Regenerate health. Forwards to `vars.regenerateHealth`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | boolean | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## soldierHealth

```lua
vars.soldierHealth([value])
```

Soldier health (%). Forwards to `vars.soldierHealth`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | number | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## bulletDamage

```lua
vars.bulletDamage([value])
```

Bullet damage (%). Forwards to `vars.bulletDamage`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | number | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## playerRespawnTime

```lua
vars.playerRespawnTime([value])
```

Player respawn time (%). Forwards to `vars.playerRespawnTime`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | number | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## playerManDownTime

```lua
vars.playerManDownTime([value])
```

Player man-down time (%). Forwards to `vars.playerManDownTime`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | number | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## onlySquadLeaderSpawn

```lua
vars.onlySquadLeaderSpawn([value])
```

Only squad leader spawn. Forwards to `vars.onlySquadLeaderSpawn`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | boolean | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## forceReloadWholeMags

```lua
vars.forceReloadWholeMags([value])
```

Force reload whole magazines. Forwards to `vars.forceReloadWholeMags`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | boolean | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## unlockMode

```lua
vars.unlockMode([value])
```

Unlock mode. Forwards to `vars.unlockMode`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | string / number / boolean | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## gunMasterWeaponsPreset

```lua
vars.gunMasterWeaponsPreset([value])
```

Gun Master weapons preset. Forwards to `vars.gunMasterWeaponsPreset`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | string / number / boolean | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## vehicleSpawnAllowed

```lua
vars.vehicleSpawnAllowed([value])
```

Vehicle spawn allowed. Forwards to `vars.vehicleSpawnAllowed`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | boolean | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## vehicleSpawnDelay

```lua
vars.vehicleSpawnDelay([value])
```

Vehicle spawn delay (%). Forwards to `vars.vehicleSpawnDelay`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | number | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## 3pCam

```lua
vars["3pCam"]([value])
```

Third-person vehicle camera. Forwards to `vars.3pCam`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | boolean | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## hud

```lua
vars.hud([value])
```

HUD. Forwards to `vars.hud`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | boolean | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## miniMap

```lua
vars.miniMap([value])
```

Minimap. Forwards to `vars.miniMap`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | boolean | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## miniMapSpotting

```lua
vars.miniMapSpotting([value])
```

Minimap spotting. Forwards to `vars.miniMapSpotting`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | boolean | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## 3dSpotting

```lua
vars["3dSpotting"]([value])
```

3D spotting. Forwards to `vars.3dSpotting`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | boolean | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## nameTag

```lua
vars.nameTag([value])
```

Name tags. Forwards to `vars.nameTag`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | boolean | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## killCam

```lua
vars.killCam([value])
```

Kill camera. Forwards to `vars.killCam`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | boolean | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## crossHair

```lua
vars.crossHair([value])
```

Crosshair. Forwards to `vars.crossHair`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | boolean | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## hitIndicatorsEnabled

```lua
vars.hitIndicatorsEnabled([value])
```

Hit indicators. Forwards to `vars.hitIndicatorsEnabled`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | boolean | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## gameModeCounter

```lua
vars.gameModeCounter([value])
```

Game mode counter (%). Forwards to `vars.gameModeCounter`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | number | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## roundTimeLimit

```lua
vars.roundTimeLimit([value])
```

Round time limit (%). Forwards to `vars.roundTimeLimit`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | number | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## ticketBleedRate

```lua
vars.ticketBleedRate([value])
```

Ticket bleed rate (%). Forwards to `vars.ticketBleedRate`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | number | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## roundStartPlayerCount

```lua
vars.roundStartPlayerCount([value])
```

Round start player count. Forwards to `vars.roundStartPlayerCount`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | number | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## roundRestartPlayerCount

```lua
vars.roundRestartPlayerCount([value])
```

Round restart player count. Forwards to `vars.roundRestartPlayerCount`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | number | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## roundLockdownCountdown

```lua
vars.roundLockdownCountdown([value])
```

Round lockdown countdown. Forwards to `vars.roundLockdownCountdown`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | number | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## roundWarmupTimeout

```lua
vars.roundWarmupTimeout([value])
```

Round warmup timeout. Forwards to `vars.roundWarmupTimeout`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | number | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## idleTimeout

```lua
vars.idleTimeout([value])
```

Idle timeout (seconds). Forwards to `vars.idleTimeout`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | number | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## idleBanRounds

```lua
vars.idleBanRounds([value])
```

Idle ban rounds. Forwards to `vars.idleBanRounds`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | number | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## teamKillCountForKick

```lua
vars.teamKillCountForKick([value])
```

Team kills before kick. Forwards to `vars.teamKillCountForKick`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | number | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## teamKillKickForBan

```lua
vars.teamKillKickForBan([value])
```

Team-kill kicks before ban. Forwards to `vars.teamKillKickForBan`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | number | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## teamKillValueForKick

```lua
vars.teamKillValueForKick([value])
```

Team-kill value before kick. Forwards to `vars.teamKillValueForKick`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | number | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## teamKillValueIncrease

```lua
vars.teamKillValueIncrease([value])
```

Team-kill value increase. Forwards to `vars.teamKillValueIncrease`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | number | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## teamKillValueDecreasePerSecond

```lua
vars.teamKillValueDecreasePerSecond([value])
```

Team-kill value decrease per second. Forwards to `vars.teamKillValueDecreasePerSecond`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | number | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## premiumStatus

```lua
vars.premiumStatus([value])
```

Premium priority. Forwards to `vars.premiumStatus`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | boolean | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## mpExperience

```lua
vars.mpExperience([value])
```

MP experience. Forwards to `vars.mpExperience`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | string / number / boolean | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## roundPlayersReadyBypassTimer

```lua
vars.roundPlayersReadyBypassTimer([value])
```

Ready bypass timer. Forwards to `vars.roundPlayersReadyBypassTimer`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | number | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## roundPlayersReadyMinCount

```lua
vars.roundPlayersReadyMinCount([value])
```

Minimum ready players. Forwards to `vars.roundPlayersReadyMinCount`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | number | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.

## roundPlayersReadyPercent

```lua
vars.roundPlayersReadyPercent([value])
```

Ready players (%). Forwards to `vars.roundPlayersReadyPercent`. No unit conversion or result parsing is applied.

| Name | Type | Description |
| --- | --- | --- |
| value | number | Optional; omit to read, provide to write. Accepted values depend on the game. |

**Returns:** `table`: one-based response words, including `"OK"` at index 1.
