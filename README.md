# Rifle Suppressor

Puts the pistol suppressor on the rifle in Sons of the Forest. Rifle shots use
the suppressed pistol shot sound. You do not need a suppressor in your inventory.

This used to be part of [RifleScope](https://github.com/JustinOros/sotf-riflescope)
and is now its own mod, so you can use either one or both.

## Multiplayer

Client side only. Nothing gets installed on a dedicated server, and other players
do not need the mod.

## Installation

### Easiest

Use the [Sons of the Forest mod loader](https://justinoros.github.io/sotf-modloader/)
in Chrome or Edge.

### Easy install

Close the game, open PowerShell and paste:

```powershell
irm https://raw.githubusercontent.com/JustinOros/sotf-riflesuppressor/main/Install.ps1 | iex
```

It finds your game, installs RedLoader if needed, and installs the latest
RifleSuppressor. Run it again any time to update.

### Manual install

1. Install [RedLoader](https://github.com/ToniMacaroni/RedLoader/releases/latest)
   into your Sons of the Forest folder and launch the game once
2. Download `RifleSuppressor.zip` from the
   [latest release](https://github.com/JustinOros/sotf-riflesuppressor/releases/latest)
3. Extract it into the `Mods` folder inside your game folder
4. You should end up with:

```
Mods\RifleSuppressor.dll
Mods\RifleSuppressor\manifest.json
```

## Usage

The suppressor is on as soon as the mod is installed. Equip the rifle and fire.

Press F1 to open the console, then use these commands:

| Command | Action |
| --- | --- |
| `suppressor` | Show whether the rifle suppressor is on or off |
| `suppressor on` | Put the suppressor on the rifle |
| `suppressor off` | Take the suppressor off the rifle |

The setting is saved to `UserData\RifleSuppressor.txt` in your game folder.

If you used the suppressor from RifleScope 1.1.1 or older, update RifleScope to
1.2.0 or newer so the two mods do not both register the `suppressor` command.

## Building from source

Requires the .NET 8 SDK and RedLoader installed with its game assemblies
generated.

```powershell
.\build.ps1 -Install
```

Use `-Package` to build `RifleSuppressor.zip` for a release. Pass `-GameDir "path"`
if the game is not found automatically.

## Troubleshooting

Check `_RedLoader\Latest.log` in your game folder. RifleSuppressor logs a line
when it loads.
