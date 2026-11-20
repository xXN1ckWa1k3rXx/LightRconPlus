# Plugin library

Place game-specific plugins in `BF3/`, `BF4/` or `BFH/` (Battlefield Hardline).

A copied plugin package uses `GAME/PluginName/PluginName.cs`, `.dll` or `.lua`. Flat game directories are also supported. Keep dependencies and Lua modules with their entrypoint. Matching source/DLL files are listed once. Procon loads a supplied matching DLL directly on first discovery; otherwise it compiles source and reuses the resulting per-server cache until compilation inputs change. Assembly metadata distinguishes plugin DLLs from dependencies, and binary `.cs` helpers are excluded from the plugin list.

Core scans the detected game's directory after connecting to RCON, when a Layer user signs in successfully, and when **Refresh list** is selected. There is no periodic file scan. Changed libraries reload runtimes while restoring saved settings and enabled states; new plugins remain disabled until explicitly enabled. Procon source is compiled in the per-server compatibility directory under the profile store, rather than writing generated DLLs into this library.

The desktop import preference controls whether files stay at their original location or are copied into the library beside the executable. Layer clients manage the upstream library; import files on the host.
