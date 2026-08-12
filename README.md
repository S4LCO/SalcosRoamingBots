# SRB - Salco's Roaming Bots

SRB is a lightweight client-side BepInEx plugin for SPT 4.1.x. It gives otherwise idle bots distant, dynamically selected NavMesh destinations while yielding to combat, danger, healing, group behavior, and higher-priority BigBrain layers.

## Requirements

- SPT 4.1.x
- BigBrain 1.5.0 or newer
- Waypoints 1.9.0 or newer

Plugin GUID: `com.salco.srb`

## Building

Open `SalcosRoamingBots.sln` in Visual Studio or run:

```powershell
dotnet build .\SalcosRoamingBots\SalcosRoamingBots.csproj -c Release
```

The project defaults to `S:\SPT_4.1.x`. Pass a different `SPTInstallPath` MSBuild property when necessary.

## Installation

Place `SalcosRoamingBots.dll` in:

```text
BepInEx/plugins/SalcosRoamingBots/
```

Settings are available through the BepInEx F12 configuration menu.

## Scope

SRB controls idle travel only. It does not replace combat AI, simulate quests, select loot, change spawns, equip bots, or force extraction.
