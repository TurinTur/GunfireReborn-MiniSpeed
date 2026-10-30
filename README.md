# MiniSpeed — Gunfire Reborn

A small configurable local-player movement-speed mod. Default: **1.5×**.

## Download and install

1. Download `MiniSpeed-v0.1.0-win-x64.zip` from the
   [GitHub releases page](https://github.com/TurinTur/GunfireReborn-MiniSpeed/releases).
2. Close the game and extract **all ZIP contents** into its installation folder,
   beside `Gunfire Reborn.exe`.
3. Launch through Steam, enter a solo run, and use **F6** to compare speeds.

The ZIP includes the loader and .NET runtime; no separate installation is needed.
It targets Windows x64 Steam installations. If another mod loader is installed,
back up its `winhttp.dll` and `doorstop_config.ini` before replacing them.
See the bundled `INSTALL.txt` for configuration and removal instructions.

**Status:** experimental. Build and hook installation verified; physical movement
and gameplay interactions still require an in-game play test.

## Current implementation

Loads through Unity Doorstop with .NET 6 and uses MinHook 1.3.4 to intercept
`PlayerProp.get_Speed`. It identifies the local player's property object through
`CharacterMotionManager.HeroObj.playerProp`, and multiplies only that object's
returned speed. The underlying stat is never overwritten, so the multiplier does
not accumulate and disabling the mod immediately returns the original value.

This is a **reported movement-speed multiplier**, not a verified modification of
the hero's base stat before every other modifier. Weapon penalties, bonuses,
dash behavior, and scene transitions need in-game verification.

The installed build uses Unity 2018.4.36f1 and nonstandard IL2CPP metadata files.
Standard BepInEx interop generation failed, so this mod uses the runtime API
directly. BepInEx plugins are not loaded while Doorstop targets MiniSpeed.

## Controls

- **F6:** toggle the multiplier for the current session.
- **F7:** reload `MiniSpeed.cfg`.

Controls are checked while the local hero's speed is being queried and the game
has focus. Hold a key briefly if necessary. Toggle/reload events appear in
`MiniSpeed.log`; there is no in-game notification.

## Configuration

`MiniSpeed.cfg` is beside `Gunfire Reborn.exe`:

```ini
[Movement]
Enabled = true
SpeedMultiplier = 1.5
```

Use a decimal point. Supported multiplier range: `0.25`–`5.0`.
Save, then press F7 during play, or restart the game.
F6 does not save to the configuration file.

## Build and deploy

Requires the .NET 8 SDK (targets .NET 6):

```powershell
dotnet build .\MiniSpeed.csproj -c Release
```

Copy `bin\Release\net6.0\MiniSpeed.dll` beside the game executable while
the game is closed. Runtime requirements in that folder:

- Unity Doorstop `winhttp.dll` and `doorstop_config.ini`.
- .NET 6 runtime in `dotnet\` (bundled with BepInEx IL2CPP build 791).
- `MinHook.x64.dll` from the official MinHook 1.3.4 binary release.

Doorstop configuration:

```ini
[General]
enabled = true
target_assembly = MiniSpeed.dll

[Il2Cpp]
coreclr_path = dotnet\coreclr.dll
corlib_dir = dotnet
```

Official dependencies:

- https://builds.bepinex.dev/projects/bepinex_be/791
- https://github.com/TsudaKageyu/minhook/releases/tag/v1.3.4

## Verification

1. Launch from Steam. `MiniSpeed.log` should report `hook installed`.
2. Enter a solo run. The log should report `Local hero speed hook active` with
   the original and modified values.
3. Walk along a repeatable route; compare with F6 enabled and disabled.
4. Check weapon switching, scroll speed bonuses, dashing, and a level transition.
5. Test `SpeedMultiplier = 1.0` and reload with F7 for an unmodified comparison.

Hook installation alone does not establish that physical movement has changed.
Actual movement and modifier interactions require the play test above.

## Disable

Set `Enabled = false` and reload with F7, or press F6 during play.
To disable all Doorstop loading, close the game and set `enabled = false` in
`doorstop_config.ini`. Re-enable it to load the mod again.

No original game binaries or save files are patched by this mod.
