# Modding examples

Worked examples for `docs/modding.md`. Each section is one complete mod: a folder with a `mod.json` and one or more
definition files. Copy the folder into your mods folder (`%APPDATA%\Godot\app_userdata\openswr\mods` by default), enable it on
the **Mods** screen (bottom-left of the main menu) and start a new game. Every example here loads without errors on the
original install, on its own and all together, and was played 100 days computer against computer.

Ids of original records (`base:…`) come from the id list: start Open-SWR with `-- --list-ids ids.txt` (see `docs/modding.md`,
Ids). Lines starting with `//` are comments; definition files allow them.

Contents:

1. [Change stats of existing units](#1-change-stats-of-existing-units)
2. [Rename a unit and change its encyclopedia text](#2-rename-a-unit-and-change-its-encyclopedia-text)
3. [Add a capital ship with a picture and a 3D model](#3-add-a-capital-ship-with-a-picture-and-a-3d-model)
4. [Add a fighter](#4-add-a-fighter)
5. [Add a troop regiment](#5-add-a-troop-regiment)
6. [Add a facility](#6-add-a-facility)
7. [Add a minor character](#7-add-a-minor-character)
8. [Add a special force and let missions use it](#8-add-a-special-force-and-let-missions-use-it)
9. [Change the starting fleet (a mod that needs another mod)](#9-change-the-starting-fleet-a-mod-that-needs-another-mod)
10. [Change a game parameter](#10-change-a-game-parameter)
11. [Add a sector with new planets](#11-add-a-sector-with-new-planets)
12. [Replace the galaxy map picture](#12-replace-the-galaxy-map-picture)
13. [Common errors and how to fix them](#13-common-errors-and-how-to-fix-them)

## 1. Change stats of existing units

`override` changes only the fields you give. Everything else keeps the original value.

```
mods/rebalance/
  mod.json
  stats.json
```

`mod.json`:

```json
{ "id": "rebalance", "name": "Tougher Empire", "version": "1.0" }
```

`stats.json`:

```json
{
  "records": [
    // Imperial I Star Destroyer: more hull, stronger forward turbolasers
    { "op": "override", "type": "capitalShip", "id": "base:capitalShip:133", "hull": 3200, "fore": { "turbolaser": 120, "ionCannon": 100, "laserCannon": 0 } },
    // TIE Interceptor: more agile
    { "op": "override", "type": "fighter", "id": "base:fighter:6", "maneuverability": 9 },
    // Stormtrooper Regiment: better attack, cheaper upkeep
    { "op": "override", "type": "troop", "id": "base:troop:6", "attackStrength": 8, "maintenanceCost": 5 },
    // Orbital Shipyard: builds faster
    { "op": "override", "type": "facility", "id": "base:shipyard:1", "processingRate": 5 }
  ]
}
```

Notes:

- Nested values change one by one too: `"fore": { "turbolaser": 120 }` changes only the forward turbolasers.
- You do not have to fix `attackStrength` and `totalAttackStrength` yourself. When a record changes an arc but not the sums,
  the loader recomputes them.
- The original values are in `docs/formats/sd-dat.md` field order. You can also read them from the encyclopedia's stat blocks.
  For reference, the Imperial I Star Destroyer has hull 2750 and fore turbolaser 100.

## 2. Rename a unit and change its encyclopedia text

```json
{ "id": "rename", "name": "Renamed frigate", "version": "1.0" }
```

`names.json`:

```json
{
  "records": [
    { "op": "override", "type": "capitalShip", "id": "base:capitalShip:67", "name": "Escort Frigate",
      "description": "A long, narrow escort ship.\nGood against fighters, weak in a line battle." }
  ]
}
```

The new name shows everywhere: map, windows, messages and encyclopedia. `\n` starts a new line in the description.

## 3. Add a capital ship with a picture and a 3D model

`from` copies every field of an existing record first. You then only list what is different.

```
mods/newship/
  mod.json
  ships.json
  art/patrol.png
  art/patrol.glb
```

```json
{ "id": "newship", "name": "Patrol cruiser", "version": "1.0" }
```

`ships.json`:

```json
{
  "records": [
    {
      "op": "add", "type": "capitalShip", "id": "patrol", "from": "base:capitalShip:132", "name": "Patrol Cruiser",
      "picture": "art/patrol.png",
      "model": "art/patrol.glb",
      "description": "A fast cruiser for guarding quiet sectors.",
      "researchOrder": 12, "researchDifficulty": 30,
      "refinedMaterialCost": 60, "maintenanceCost": 35,
      "hull": 1200, "shieldStrength": 200, "hyperdrive": 60,
      "fore": { "turbolaser": 40, "ionCannon": 20, "laserCannon": 10 },
      "fighterCapacity": 2, "troopCapacity": 1
    }
  ]
}
```

Notes:

- The copy comes from `base:capitalShip:132` (Victory I Star Destroyer), an Empire ship. So the new ship is Empire only.
  For an Alliance ship, copy an Alliance ship or set `"alliance": 1, "empire": 0`.
- **Research order.** Each side can have only one unit per research order and producer. The shipyard builds both
  capital ships and fighters, so they share the numbers. In the original data, orders 1–11 are taken for both sides at the
  shipyard; 12 is free. Order 0 means "available from the start". If you pick a taken order, the error lists the records
  that use it.
- `picture`: any PNG with the 122×50 shape (244×100 and larger work too and look sharper). Transparent pixels show what
  lies behind.
- `model`: a glTF 2.0 file (`.glb`, or `.gltf` with its files beside it), +y up, nose toward −z, 1 unit = 1 battle unit.
  Original ships are 0.6 to 8 units long. Without `model` (or when the file does not load), a new ship is drawn as a box.
  Details: `docs/modding.md`, "3D ship models".
- `picture` and `model` are optional. Without `picture`, the ship has no picture.

## 4. Add a fighter

```json
{ "id": "newfighter", "name": "Heavy fighter", "version": "1.0" }
```

`fighters.json`:

```json
{
  "records": [
    { "op": "add", "type": "fighter", "id": "lancer", "from": "base:fighter:3", "name": "Lancer Heavy Fighter",
      "researchOrder": 12, "researchDifficulty": 20,
      "shieldStrength": 2, "maneuverability": 5,
      "fore": { "turbolaser": 0, "ionCannon": 2, "laserCannon": 7 } }
  ]
}
```

Copied from the X-wing (`base:fighter:3`), so it is an Alliance fighter. `squadronSize` (the number of craft per squadron)
must stay above 0.

## 5. Add a troop regiment

```json
{ "id": "newtroop", "name": "Garrison troops", "version": "1.0" }
```

`troops.json`:

```json
{
  "records": [
    { "op": "add", "type": "troop", "id": "garrison", "from": "base:troop:8", "name": "Garrison Regiment",
      "researchOrder": 3, "researchDifficulty": 25,
      "attackStrength": 3, "defenseStrength": 9, "bombardmentDefense": 8 }
  ]
}
```

Troops are built by training facilities, so their research orders are counted separately from ships. Empire troops use
orders 1 and 2 in the original; 3 is free.

## 6. Add a facility

```json
{ "id": "newbattery", "name": "Heavy battery", "version": "1.0" }
```

`facilities.json`:

```json
{
  "records": [
    { "op": "add", "type": "facility", "id": "heavybattery", "from": "base:ionCannon:1", "name": "Heavy Planetary Battery",
      "researchOrder": 12, "researchDifficulty": 50,
      "refinedMaterialCost": 9, "maintenanceCost": 6, "attackStrength": 3500 }
  ]
}
```

With `from`, the new facility takes the family of the copied record (here `ionCannon`, a planetary battery) and its role. Without
`from`, set `"family"` yourself (see the table in `docs/modding.md`). You cannot invent a new facility role.

## 7. Add a minor character

```json
{ "id": "newcharacter", "name": "New officer", "version": "1.0" }
```

`characters.json`:

```json
{
  "records": [
    { "op": "add", "type": "minorCharacter", "id": "drask", "name": "Commander Drask",
      "alliance": 0, "empire": 1,
      "diplomacy": { "base": 20, "variance": 10 }, "espionage": { "base": 30, "variance": 20 },
      "combat": { "base": 60, "variance": 20 }, "leadership": { "base": 70, "variance": 15 },
      "loyalty": { "base": 90, "variance": 10 },
      "jediProbability": 0,
      "canBeAdmiral": 1, "canBeCommander": 1 }
  ]
}
```

Notes:

- A character belongs to exactly one side: one of `alliance` / `empire` is 1, the other 0.
- Each skill is rolled once per game as `base` + a random 0…`variance`.
- Skills you leave out are 0. New major characters are not possible.

## 8. Add a special force and let missions use it

A special force can only run missions that list its bit. So a new force needs two changes: its own `missionMask` bit, and
that bit added to each mission it may run.

```json
{ "id": "newforce", "name": "Slicer team", "version": "1.0" }
```

`forces.json`:

```json
{
  "records": [
    { "op": "add", "type": "specialForce", "id": "slicers", "from": "base:specialForce:2", "name": "Slicer Team",
      "missionMask": 16,
      "espionage": { "base": 65, "variance": 0 }, "combat": { "base": 10, "variance": 0 } },
    // Espionage (520) and Sabotage (1026) also accept the new force's bit 16
    { "op": "override", "type": "mission", "id": "base:espionage:19", "specialForceMask": 536 },
    { "op": "override", "type": "mission", "id": "base:sabotage:18", "specialForceMask": 1042 }
  ]
}
```

Notes:

- The original forces use bits 1, 2, 4, 8 (Alliance) and 256, 512, 1024, 2048, 4096 (Empire). Free bits
  include 16, 32, 64 and 128.
- `specialForceMask` replaces the whole value, so add your bit to the original mask. Original masks of the main missions: Espionage 520,
  Sabotage 1026, Reconnaissance 260, Diplomacy 0, Incite Uprising 1025, Subdue Uprising 1025, Rescue 2050,
  Abduction 2050, Assassination 2048, Death Star Sabotage 2.
- The computer opponent never builds mod special forces. It still uses ones it gets as agents.

## 9. Change the starting fleet (a mod that needs another mod)

This mod gives the Empire two Patrol Cruisers from example 3 in its starting fleet. It names the other mod's record in
full (`newship:patrol`), so it must load after `newship`. `requires` makes sure of that.

```json
{ "id": "startfleet", "name": "Bigger Empire start", "version": "1.0", "requires": ["newship"] }
```

`tables.json`:

```json
{
  "records": [
    // the Empire fleet places entries with keys from EmpireFleetUnitsFirstKey to EmpireFleetUnitsLastKey (originally 1 to 1): let it place key 2 too
    { "op": "override", "type": "generalParam", "id": "EmpireFleetUnitsLastKey", "value": 2 },
    { "op": "replace", "type": "seedTable", "id": "CMUNEFTB", "entries": [
        { "key": 1, "count": 1, "items": ["base:capitalShip:133",
            "base:fighter:5", "base:fighter:5", "base:fighter:5", "base:fighter:5", "base:fighter:5", "base:fighter:5",
            "base:troop:6", "base:troop:6", "base:troop:6"] },
        { "key": 2, "count": 2, "items": ["newship:patrol", "base:fighter:6"] } ] }
  ]
}
```

How it reads:

- A table is always replaced as a whole. Entry 1 is the original fleet: a Star Destroyer carrying six TIE Fighter
  squadrons and three Stormtrooper regiments.
- In each entry, the first item is the ship that carries the rest. Entry 2 places `count` = 2 copies of "a Patrol Cruiser
  carrying one TIE Interceptor squadron".
- The starting tables place only the entries whose keys lie in a fixed key range. For the Empire fleet that range is general
  parameters `EmpireFleetUnitsFirstKey` and `EmpireFleetUnitsLastKey`, originally 1 to 1. Raising `EmpireFleetUnitsLastKey`
  to 2 places entry 2 as well. The Alliance fleet (`CMUNAFTB`) uses `AllianceFleetUnitsFirstKey` and `AllianceFleetUnitsLastKey`, originally 1 to 2. The other tables are listed in `docs/modding.md`, Tables, and their
  key ranges in `docs/spec/new-game.md` section 5.3.
- Load order: on the Mods screen, `newship` must be above `startfleet`. Otherwise the error is
  `startfleet mod.json: requires: mod 'newship' must come before 'startfleet' in the load order.`

## 10. Change a game parameter

Side parameters `MinorCharactersSmall` / `MinorCharactersMedium` / `MinorCharactersLarge` are the number of random minor characters each side gets at the start, for galaxy
size small / medium / large (originally 1 / 2 / 4). This mod gives each side two in a small galaxy.

```json
{ "id": "moreminors", "name": "More random characters", "version": "1.0" }
```

`params.json`:

```json
{
  "records": [
    { "op": "override", "type": "sideParam", "id": "MinorCharactersSmall", "values": [2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2] }
  ]
}
```

`values` lists every column: 8 game-setting columns for each of the two sides. `"value": 2` instead sets all columns at
once. Parameter names are the enum names in `src/OpenSWR.Data/Dat/Parameters.cs`; their meaning is in the specs, mostly `docs/spec/new-game.md`.

## 11. Add a sector with new planets

The original game already uses all 200 system slots. To add planets, first remove as many. This mod removes one sector that
only exists in the large galaxy (Outer Rim North, `base:sector:21`) with its ten systems. It then adds a new rim sector with
three planets that exists in every galaxy size.

```json
{ "id": "newsector", "name": "Ember Drift", "version": "1.0" }
```

`galaxy.json`:

```json
{
  "records": [
    // free ten system slots: remove a galaxy-size-3 sector and its ten systems
    { "op": "remove", "type": "sector", "id": "base:sector:21" },
    { "op": "remove", "type": "system", "id": "base:rimSystem:110" },
    { "op": "remove", "type": "system", "id": "base:rimSystem:111" },
    { "op": "remove", "type": "system", "id": "base:rimSystem:112" },
    { "op": "remove", "type": "system", "id": "base:rimSystem:113" },
    { "op": "remove", "type": "system", "id": "base:rimSystem:114" },
    { "op": "remove", "type": "system", "id": "base:rimSystem:115" },
    { "op": "remove", "type": "system", "id": "base:rimSystem:116" },
    { "op": "remove", "type": "system", "id": "base:rimSystem:117" },
    { "op": "remove", "type": "system", "id": "base:rimSystem:118" },
    { "op": "remove", "type": "system", "id": "base:rimSystem:119" },
    // a new rim sector in every galaxy size; systems lie in x 160–238, y 500–602
    { "op": "add", "type": "sector", "id": "drift", "name": "Ember Drift", "group": 2, "galaxySize": 1, "x": 160, "y": 500 },
    { "op": "add", "type": "system", "id": "cinder", "name": "Cinder", "sector": "drift", "x": 172, "y": 512, "systemPicture": 5 },
    { "op": "add", "type": "system", "id": "ashfall", "name": "Ashfall", "sector": "drift", "x": 201, "y": 538, "systemPicture": 17 },
    { "op": "add", "type": "system", "id": "slag", "name": "Slag Reach", "sector": "drift", "x": 184, "y": 579, "systemPicture": 42 }
  ]
}
```

Notes:

- Map coordinates run 0–1023 in both directions over the whole galaxy map.
- A system must lie in its sector's box: x from the sector's `x` to `x` + 78, y from `y` to `y` + 102.
- `group`: 1 = core (its systems are core worlds), 2 or 3 = rim.
- `galaxySize`: the smallest galaxy size that has the sector (1 small, 2 medium, 3 large).
- `systemPicture` (1–200) picks one of the original planet pictures. Add `"picture": "art/cinder.png"` (a 37×37-shaped PNG)
  to use your own.
- At most 10 systems per sector. A sector with fewer than 10 works in Open-SWR, but would crash the original game.
- To find which systems belong to a sector, look at the system list from `--list-ids`, or at a sector window in the game.

## 12. Replace the galaxy map picture

```
mods/galaxyart/
  mod.json
  art/galaxy.png
```

```json
{ "id": "galaxyart", "name": "New galaxy map", "version": "1.0", "galaxyPicture": "art/galaxy.png" }
```

A mod can consist of only `mod.json`. The picture must have the map's 607×437 shape (1214×874 and 2428×1748 work too).
Sectors and systems keep their positions over it. When several enabled mods set a galaxy picture, the last one in the load
order wins.

## 13. Common errors and how to fix them

When the enabled mods have errors, the main menu and the Mods screen list all of them, and no new game starts. Each error
names the mod, file, record and field.

| Error | Fix |
|---|---|
| `researchOrder: order 3 is used by base:capitalShip:143, base:fighter:6 for the Empire at producer shipyard; one record per order.` | Pick a free research order (example 3 notes). |
| `requires: mod 'newship' is not enabled.` | Enable the required mod too. |
| `requires: mod 'newship' must come before 'startfleet' in the load order.` | Move the required mod up on the Mods screen. |
| `no free system Index left in 100–299` | Remove as many systems as you add (example 11). |
| `x / y: (900, 678) must lie in its sector's box: x 311–389, y 631–733.` | Move the system into its sector's box. |
| `missionMask: 1 shares bits with base:specialForce:1 (1); each force needs its own bit.` | Use a free bit (example 8). |
| `cannot be removed: the game code refers to … directly` | That record can be changed, not removed. |
| `colour: unknown field for SECTORSD records` | Check the field name against the tables in `docs/modding.md`. |
| `Save game was played with mods: …` | Load the save with the same mods, versions and order it was made with. |

Changing mods never affects a game in progress. The new list applies to the next new game.
