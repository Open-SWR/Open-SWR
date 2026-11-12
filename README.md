# Open-SWR

An open-source reimplementation of a 1998 galactic 4X strategy game, built with Godot 4 and C#. The code name and code identifiers are `OpenSWR`.

**You need your own copy of the original game.** This repository contains no original assets. The game reads bitmaps, sounds, movies, text and data tables from your installation at runtime. Development uses the GOG release with the 25th Anniversary 1.02 mod. A plain GOG install also works through fallbacks, but has had less testing.

## What is there

- Readers for all of the original data formats: PE resources, bitmaps, RLE sprites, `.x` models, Smacker movies, and the `*SD.DAT` / `*TB.DAT` tables.
- The strategy simulation: new-game setup, day tick, economy, production, fleets, missions, prisoners, uprisings, research, Jedi, story, Death Star, victory, knowledge views, messages, and save / load.
- Combat: assaults, bombardment, the automatic space battle engine, and the tactical 3-D battle.
- The original computer opponent, plus the project's own ("OpenSWR AI").
- The front end, the strategy screen with its windows, the tactical screen, and a mod loader (`docs/modding.md`).

## Build

Requirements:
- .NET SDK 10 (the libraries target `net8.0`, the tests `net10.0`);
- Godot 4.7 .NET (mono) build;
- the original game installed.

```
dotnet build OpenSWR.sln
```

Run the game from the Godot editor (`godot/project.godot`), or from the command line with the Godot .NET binary:

```
godot --path godot
```

On first start the launcher asks for the game folder (the folder that contains `STRATEGY.DLL`). You can also set `OPENSWR_GAME_DIR`.

## Tests

```
OPENSWR_GAME_DIR="<game dir>" dotnet test tests/OpenSWR.Tests
```

Tests that need the game files skip when the environment variables are missing; `OPENSWR_REQUIRE_GAME=1` turns those skips into failures. Some tests also need `OPENSWR_DUMP_DIR` (a resource dump of your install), which is not part of this repository.

## Layout

```
src/OpenSWR.Data     data readers (no Godot dependency)
src/OpenSWR.Sim      game state and rules (no Godot dependency)
tests/OpenSWR.Tests  test suite
godot/               Godot project: screens, UI, tactical 3-D
tools/aimatch        headless AI-against-AI matches
tools/frida          tracing tool for the original executable (observation only)
tools/drawdiff       compares drawing logs of the original and Open-SWR
docs/modding.md      modding guide
```

## Clean room

The code is written from behaviour specifications and tests, not from decompiled code. Neither the specifications nor any decompiled code are in this repository.

## Legal

This is a fan project, not affiliated with or endorsed by the rights holders of the original game. All trademarks belong to their owners.

The code is licensed under the GNU General Public License v3.0 (`LICENSE`).
