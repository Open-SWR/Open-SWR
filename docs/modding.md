# Modding Open-SWR

Open-SWR loads its game data from your copy of the original game. A **mod** is a folder of JSON files that changes that data
before a game starts: new capital ships, fighters, troops, facilities, special forces and minor characters, changed stats, new
planets (systems), new or removed sectors, other starting units and parameters. Mods change data, never rules: when a mod needs
something the game's mechanics cannot do, Open-SWR refuses the mod with an error that names the mod, file, record and field.

The limits below come from the original game code (`docs/spec/mod-limits.md`). The format itself is Open-SWR's own design.

## Quick start

```
mods/
  mymod/
    mod.json
    ships.json
    galaxy.json
    art/picket.png
```

`mod.json`:

```json
{ "id": "mymod", "name": "My first mod", "version": "1.0", "requires": [], "conflicts": [] }
```

`ships.json`:

```json
{
  "records": [
    { "op": "add", "type": "capitalShip", "id": "picket", "from": "base:capitalShip:65", "name": "Warden Picket",
      "hull": 900, "researchOrder": 12, "researchDifficulty": 100, "fore": { "turbolaser": 30 } }
  ]
}
```

Enable it:

- the **Mods** button in the bottom-left corner of the main menu opens the mods screen: every mod folder found, a box to
  enable it, Up / Down for the load order; **Save** writes the list and test-loads it, listing any errors. The new list applies
  to the next new game (a running game keeps its mods), or
- environment variable `OPENSWR_MODS=mymod` (several: `OPENSWR_MODS=mymod,othermod`, in load order; overrides the saved list), or
- `[mods] enabled="mymod,othermod"` in `user://settings.cfg` (Windows: `%APPDATA%\Godot\app_userdata\openswr\settings.cfg`;
  what the mods screen writes).

The mods folder is `OPENSWR_MODS_DIR`, else `[mods] dir=` in the same file, else `user://mods`
(`%APPDATA%\Godot\app_userdata\openswr\mods`). Errors appear on the main menu and the mods screen; the strategy screen does not
start a game while the enabled mods have errors.

Step-by-step examples for each kind of change (stats, new units, characters, special forces, starting fleet, parameters,
sectors, galaxy picture) are in `docs/modding-examples.md`.

A complete working example (one new sector with five planets, one with its own picture,
a capital ship with a picture, encyclopedia text and a model file, a fighter, a minor character, a changed starting fleet and a
galaxy background) is the test mod in `tests/OpenSWR.Tests/Mods/testmod/`.

## mod.json

| Key | Required | Meaning |
|---|---|---|
| `id` | yes | Same as the folder name: lower-case letters, digits, `_`, `-`; not `base`. |
| `version` | yes | Any string. Saves record it (see Saves). |
| `name` | no | Display name. |
| `requires` | no | Mod ids that must be enabled **before** this one. |
| `conflicts` | no | Mod ids that must not be enabled with this one. |
| `galaxyPicture` | no | A PNG inside the mod folder that replaces the galaxy map background (see Pictures). |

## Definition files

Every other `*.json` file in the mod folder (sub-folders included) is a definition file, read in path order
(ordinal, so `10-ships.json` before `20-tables.json`). Each holds `{"records": [ … ]}`; records apply in order, mods in load
order. Comments (`//`) and trailing commas are allowed.

A record:

| Key | Meaning |
|---|---|
| `op` | `add`, `override` (change the given fields only), `remove`; tables: `replace`; parameters: `override` |
| `type` | `sector`, `system`, `capitalShip`, `fighter`, `troop`, `facility`, `specialForce`, `minorCharacter`, `majorCharacter`, `mission`, `table`, `seedTable`, `generalParam`, `sideParam` |
| `id` | the record: see Ids |
| `from` | add only: copy every field of this record first (same type), then apply the fields given |
| `name` | display name (required on add; on override it renames the record) |
| `picture` | a `.png` file inside the mod folder: the record's picture (see Pictures) |
| `description` | encyclopedia text of the record (plain text, `\n` for a line break) |
| `model` | a `.glb` / `.gltf` file inside the mod folder: the 3D model of a capital ship in tactical battles |
| `family` | add only: the record's family, by name (e.g. `capitalShip`) or decimal number (see Families) |
| `producer` | `none`, `shipyard`, `training` or `construction`: who builds it |
| any other key | a field of the record (tables below). Nested objects set grouped fields: `"fore": {"turbolaser": 30}` |

Numbers are JSON integers ≥ 0; `true` / `false` = 1 / 0. Families and parameters are named (see Families and Parameters).

### Ids

- Records of the original game: `base:<family>:<index>`: the family by name (see Families), the index in decimal:
  `base:capitalShip:65` (capital ship 65), `base:coreSystem:265` (the Empire capital), `base:sector:22` (a sector). The SD file
  layouts are in `docs/formats/sd-dat.md`; the encyclopedia lists every unit.
- Records added by a mod: `<mod id>:<name>`. Inside the same mod the plain name works: `"sector": "drift"` means
  `mymod:drift`. Other mods' records are named in full: `"id": "mymod:picket"`.
- An added record gets the next free Index of its file (systems: the lowest free Index in 100–299), so its SdRef depends on the
  mods before it. Ids never change; use ids, not SdRefs, for mod records.
- The list of every `base:` id with its type and name: start Open-SWR with `-- --list-ids ids.txt` (from a build:
  `Open-SWR.exe -- --list-ids ids.txt`; from the repo: `Godot_v4.7.2-stable_mono_win64_console.exe --headless --path godot -- --list-ids ids.txt`).
  It writes one line per record, `base:capitalShip:65<tab>capitalShip<tab><name>`, and quits. The game folder must be known
  already (`OPENSWR_GAME_DIR` or a previous start); otherwise Open-SWR asks for it first.

### Families

The family is the record type in an id (`base:<family>:<index>`) and the `family` of an added record. Names:

| Name | Family | Records |
|---|---|---|
| `sector` | 128 | galaxy sectors |
| `coreSystem` | 144 | systems of core sectors (group 1) |
| `rimSystem` | 146 | systems of rim sectors (groups 2 and 3) |
| `troop` | 16 | troop types |
| `capitalShip` | 20 | capital ships |
| `deathStar` | 24 | Death Star class ships |
| `fighter` | 28 | fighter squadrons |
| `headquarters` | 32 | headquarters |
| `ionCannon` | 34 | planetary batteries |
| `gun` | 35 | defences (counted with the batteries) |
| `planetaryShield` | 36 | planetary shields |
| `deathStarShield` | 37 | Death Star shields |
| `shipyard` | 40 | manufacturing: ships and fighters |
| `trainingFacility` | 41 | manufacturing: troops and special forces |
| `constructionYard` | 42 | manufacturing: facilities |
| `mine` | 44 | mines |
| `refinery` | 45 | refineries |
| `majorCharacter` | 48 | major characters (`leia` 49, `luke` 50, `han` 51, `palpatine` 52, `vader` 53) |
| `minorCharacter` | 56 | minor characters |
| `specialForce` | 60 | special forces |

Missions: `move` 65, `return` 66, `autorouting` 67, `adrift` 68, `diplomacy` 81, `espionage` 82, `research` 83,
`reconnaissance` 84, `recruitment` 85, `inciteUprising` 86, `subdueUprising` 87, `jediTraining` 88, `rescue` 97,
`abduction` 98, `assassination` 99, `palace` 100, `bounty` 101, `sabotage` 105, `deathStarSabotage` 106, `dagobah` 113,
`vacation` 114, `pickup` 115 (`mission` records; a mission's family is its kind).

Other families are in the install but no mod adds or names them: `unknownFleet` 0, `fleet` 8, `secondHeadquarters` 33,
`abode` 152, `constructionManager` 160, `shipyardManager` 162, `trainingManager` 164, `unmapped` 241, `staging` 242,
`side` 243, `missionManager` 248, `gameManager` 249, `characterManager` 250. A family without a name is written as its number.
A `family` value may be a name or a decimal number from 0 to 255.

### Operations

- `add`: a new record. `name` is required; unset fields are 0 unless `from` copies them. Defaults: `producer` per type
  (ships / fighters shipyard, troops / special forces training, facilities construction, others none), new systems
  `systemPicture` 1.
- `override`: changes only the fields given. `family` and `from` are refused (a family change would change the record's SdRef
  and break every reference to it).
- `remove`: deletes the record. References to it then fail the checks (e.g. a system in a removed sector). Records the game
  code names directly cannot be removed (list under Limits).

## Record types and fields

Field names follow `docs/formats/sd-dat.md`; `unkNN` fields have no known meaning and keep the copied or zero value. Every
unit type has the **unit base**: `alliance`, `empire` (1 = available to that side), `refinedMaterialCost` (0 = not buildable),
`maintenanceCost`, `researchOrder`, `researchDifficulty` (0–100).

| type | families (add) | fields |
|---|---|---|
| `sector` | `sector` | `group` (1 core, 2 / 3 rim), `galaxySize` (1–3: the smallest galaxy size that has it), `x`, `y` (0–1023, map position) |
| `system` | follows its sector: group 1 → `coreSystem`, 2 / 3 → `rimSystem` | `sector` (id, required on add), `x`, `y`, `systemPicture` (1–200: one of the original system pictures), `unk20`, `unk28` |
| `capitalShip` | `capitalShip`, `deathStar` (Death Star class) | unit base, `unk30`, `unk34`, `shieldStrength`, `unk3C`, `maneuverability`, `hyperdrive`, `hyperdriveIfDamaged`, weapon groups `fore` / `aft` / `port` / `starboard` / `range` / `attackStrength` each `{turbolaser, ionCannon, laserCannon}`, `totalAttackStrength`, `hull` (> 0), `tractorBeamPower`, `tractorBeamRange`, `gravityWell`, `interdictionStrength`, `bombardment`, `damageControl`, `weaponRechargeRate`, `shieldRechargeRate`, `fighterCapacity`, `troopCapacity`, `unkC4` |
| `fighter` | `fighter` | as capital ships up to `totalAttackStrength`, then `unk98`, `unk9C`, `squadronSize` (> 0), `unkA4` |
| `troop` | `troop` | unit base, `unk30`, `unk34`, `bombardmentDefense`, `attackStrength`, `defenseStrength` |
| `facility` | `ionCannon` / `gun` batteries, `planetaryShield`, `deathStarShield`: unit base, `bombardmentDefense`, `attackStrength`, `shieldStrength`; `shipyard`, `trainingFacility`, `constructionYard`, `mine`, `refinery`: unit base, `bombardmentDefense`, `processingRate` (> 0) | `family` required on add (or `from`) |
| `specialForce` | `specialForce` | unit base, skills, `missionMask` (one bit of its own) |
| `minorCharacter` | `minorCharacter` | `alliance`, `empire` (exactly one = 1), skills, `jediProbability` (0–100), `isKnownJedi`, `jediLevel` `{base, variance}`, `canBeAdmiral`, `canBeCommander`, `canBeGeneral`, `isUnableToBetray`, `isJediTrainer`, `unk20` … `unk2C` |
| `majorCharacter` | override only | as minor characters |
| `mission` | override only | `alliance`, `empire`, `specialForceMask` |

Skills: `diplomacy`, `espionage`, `shipDesign`, `troopTraining`, `facilityDesign`, `combat`, `leadership`, `loyalty`, each
`{base, variance}`; a character rolls `base + random(0 … variance)` at placement.

Weapon sums: the original data keeps `attackStrength` = the sum of the four arcs and `totalAttackStrength` = the sum of
`attackStrength`. When a record sets an arc but not the sums, the loader recomputes them.

A new special force needs its own `missionMask` bit, and the missions it may run need that bit in their
`specialForceMask` (`mission` override). The computer opponent never builds mod special forces (it uses them as agents).

### Tables

`table` (threshold tables keyed by a roll, value = number or record id) and `seedTable` (starting units) can only be replaced
as a whole; keys must ascend.

```json
{ "op": "replace", "type": "seedTable", "id": "CMUNAFTB", "entries": [
    { "key": 1, "count": 1, "items": ["picket", "base:fighter:3", "base:fighter:3"] },
    { "key": 2, "count": 1, "items": ["base:capitalShip:70", "base:troop:1", "base:troop:1"] } ] }
```

Seed entries: `count` copies of the item list; `items[0]` is the container of the other items (a capital ship) or `null`
(the items go to the target system / fleet itself). Tables and what they place: `SYFCCRTB` / `SYFCRMTB` random facilities of
core / rim systems (value = facility id or 0, roll 0–100), `CMUNALTB` / `CMUNEMTB` Alliance / Empire starting units (roll
1–100, each entry must cost maintenance), `CMUNYVTB` / `CMUNHQTB` / `CMUNCRTB` units at the Alliance base / headquarters /
Empire capital, `CMUNAFTB` / `CMUNEFTB` the Alliance / Empire starting fleet, `FACLHQTB` / `FACLCRTB` facilities at the
headquarters / capital. Items of a side's table must be available to that side. Mission and event tables (`*MSTB`, `UPRIS*TB`,
…) take numbers: see `docs/formats/tb-dat.md`.

### Parameters

```json
{ "op": "override", "type": "generalParam", "id": "CorePopulatedChance", "value": 50 }
{ "op": "override", "type": "sideParam", "id": "MinorCharactersSmall", "values": [0, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4] }
```

`value` sets every column; `values` gives all of them (general: 8 columns by game setting; side: 8 × the two sides,
Alliance first). A parameter is named by its enum name in `src/OpenSWR.Data/Dat/Parameters.cs` (`GeneralParam` for
`generalParam`, `SideParam` for `sideParam`), or given by its decimal id. Their meaning: the specs (`docs/spec/new-game.md`
lists the setup ones, e.g. side parameters `MinorCharactersSmall` / `MinorCharactersMedium` / `MinorCharactersLarge` = random
minor characters per galaxy size, `SetupLoyalShare` / `SetupDisloyalShare` = control quotas).

## Making a galaxy

- A sector exists in galaxy sizes ≥ its `galaxySize`; its systems exist with it. Sector and system counts per galaxy size are
  free within the limits below (the original: 10 / 15 / 20 sectors of 10 systems).
- **At most 200 systems in total**, all galaxy sizes together. The original game already uses all 200, so a mod that adds
  planets must remove as many first (the test mod removes one galaxy-size-3 sector and its ten systems, then adds five).
- **At most 10 systems per sector** (the computer player's and the advisor's sector records hold 10).
- **Fewer than 10 systems in a sector works in Open-SWR only.** The original game always expects exactly 10 and crashes at
  game start (the computer player's first look at that sector) when a sector has fewer. Open-SWR accepts it without an error;
  the computer player's "all ten systems" sector summaries just never fire for that sector.
- A system lies in its sector's box: x from the sector's `x` to `x` + 78, y from `y` to `y` + 102 (sector window).
- Sector `group` 1 = core: its systems are core systems (family `coreSystem`); groups 2 / 3 = rim (`rimSystem`).
- Each galaxy size needs the Empire capital (`base:coreSystem:265`) and the Alliance base (`base:rimSystem:289`) — their sectors must keep
  `galaxySize` 1 — at least one more rim system (the headquarters is drawn from them) and enough core systems for the control
  quotas (side parameters `SetupLoyalShare` / `SetupDisloyalShare`, percent of the core systems; the Empire's quota must be at least 1).
- Initial ownership and support follow the original setup rules: the quotas above, the core / rim support parameters, the
  starting-unit tables. A mod cannot hand-place an owner per system.

## Limits (what a mod cannot do)

Refused by the loader:

- **New kinds of things.** No new major characters (each has story code), no new mission types, no new facility roles, no
  family outside the lists above, no second headquarters type, no new SD files or tables.
- **Removing records the game code names directly**: the ten troop types `base:troop:1`–`base:troop:10`, capital ships
  `base:capitalShip:74` and `base:capitalShip:130` (AI research signals), the headquarters `base:headquarters:1`, the Death Star shield
  `base:deathStarShield:4`, the mine `base:mine:1` and refinery `base:refinery:2`, the six major characters, minor characters
  `base:minorCharacter:833`, `:835`, `:836`, `:896`, `:897`, `:898`, `:899`, `:908`, special forces `base:specialForce:1`–`base:specialForce:9`,
  systems `base:coreSystem:265` and `base:rimSystem:289`. Their stats may change. The fixed characters must keep their side.
- More than 200 systems; galaxy sizes other than 1–3; anything the checks below refuse.

Accepted but limited by the original code:

- The computer opponent builds only the original mine and refinery types and the eight original special forces.
- A Death Star class ship (family `deathStar`) follows every Death Star rule (one per system, planet destruction).
- Changing stats changes the computer opponent's choices (it ranks units by their stats).
- Any change shifts random draws and object ids, so recorded games of the original no longer replay with mods.

## Checks

After loading, the whole definition set is checked (the original data passes every check). Each error names where the record
was last changed (`<mod> <file>: <id>`, or `base:<family>:<index>` for an unchanged original record) and the field:

| Check | Example error |
|---|---|
| family has an object class | `mymod ships.json: mymod:x: family: capitalShip family must be one of capitalShip, deathStar (…)` |
| Index unique per file; systems 100–299, at most 200 | `mymod galaxy.json: mymod:x: no free system Index left in 100–299 (…)` |
| fixed references exist, fixed characters keep their side | `mymod a.json: base:coreSystem:265: cannot be removed: the game code refers to base:coreSystem:265 directly (…)` |
| sector group, galaxy size, position | `mymod a.json: base:sector:22: galaxySize: 1, 2 or 3 (…)` |
| core / rim matches the sector group | `… base:rimSystem:130: sector: family rimSystem in a sector of group 1: …` |
| at most 10 systems per sector | `… base:sector:22: systems: 11 systems in this sector, at most 10 (…)` |
| system inside its sector's box | `… x / y: (900, 678) must lie in its sector's box: x 311–389, y 631–733.` |
| system picture 1–200 | `… systemPicture: 1–200 (one of the install's system pictures).` |
| capital and Alliance base in every galaxy size | `… base:coreSystem:265: sector: its sector must have galaxySize 1 (…)` |
| setup per galaxy size and setting: ≥ 2 rim systems, control quotas fit, Empire quota ≥ 1, enough random minor characters | `galaxy size 1, settings column 1: 30 core systems give the Empire a quota of 0; the Empire capital (base:coreSystem:265) needs 1 (…)` |
| producer matches the type; at least one side; research difficulty 0–100 | `… producer: TROOPSD records are built by family trainingFacility ("training") or none.` |
| one record per research order, side and producer | `… researchOrder: order 2 is used by base:capitalShip:64, base:capitalShip:143 for the Alliance at producer shipyard; one record per order.` |
| hull, squadron size, processing rate > 0 | `… hull: must be > 0.` |
| timer bases and divisors in the general parameters: base ≥ 1, variance ≥ 0, divisor ≥ 1 | `general parameter DagobahDays, column 1: 0 must be at least 1.` |
| characters: exactly one side, Jedi probability 0–100 | `… alliance / empire: a character belongs to exactly one side.` |
| special forces: own mission-mask bit | `… missionMask: 1 shares bits with base:specialForce:1 (1); each force needs its own bit.` |
| tables: keys ascend, ids resolve, facility tables hold facilities, seed items fit the side, starting-unit entries cost maintenance | `… SYFCCRTB entries[1].key: keys must ascend.` |
| fields: known name, integer in range, references resolve | `… x: a decimal JSON integer 0–65535.`, `… colour: unknown field for SECTORSD records`, `… sector: no record 'mymod:nowhere'.` |
| manifests: version, id = folder, requires before, no conflicts | `mymod mod.json: requires: mod 'core' must come before 'mymod' in the load order.` |

## Saves

A save made with mods lists them (`"mods": [{"id": …, "version": …}]`). Loading it needs the same mods, same versions, same
order; otherwise Open-SWR refuses with `Save game was played with mods: mymod 1.0; active mods: none. …`. Saves without mods
have no `mods` field and need no mods.

## Pictures, names, encyclopedia

- `name` replaces the record's name everywhere the game shows it (map, windows, messages, encyclopedia).
- `picture` replaces the record's picture in the fleet / planet / manufacturing / missions windows, the status window, the
  production and mission windows, drag images and the encyclopedia; on a system it replaces the sector-window planet picture
  (also the mission window's target). It works on added records and on `override`s of original ones. The PNG is scaled (aspect
  kept) to the size the original pictures of that kind have in each place, so draw at any resolution with the same shape:
  units, special forces and facilities 122×50, characters 80×80 (some lists use a 61×25 / 66×25 form of the same
  picture), system pictures 37×37. A 2× or 4× PNG looks sharper and draws at the same size. Transparent pixels show what lies behind. A mod picture has no
  separate en-route / damaged / captured variants; the game's generic overlays (frames, markers) are still drawn over it.
- Without `picture`, an added record shows no picture; an added system shows the original picture `systemPicture` (1–200).
- `description` is the record's encyclopedia text. Added records appear in the encyclopedia under their mod name, with their
  description and picture (or none); a `description` on an original record replaces its text.
- `galaxyPicture` (in `mod.json`) replaces the galaxy map. It must have the map's shape: 607×437 or the same proportions larger
  (1214×874, 2428×1748): it is drawn into the same 607×437 area, so every sector and system keeps its position (system x / y 0–1023 map
  to the picture's width / height). The map display filters show it darkened. When several mods have one, the last in the
  load order wins.
- Missing files, files outside the mod folder, non-PNG pictures and a galaxy picture of the wrong shape are load errors that
  name the mod, file, record and field. A PNG that passes the check but still cannot be decoded is reported in the log and the
  original picture (or none) is drawn.
- `model` files are checked (extension, inside the folder, present). The tactical battle screen draws a capital ship's model in
  place of the game's own; a file that does not load, or has no mesh, falls back to the game's model (or a box for a new class).
  Axes, scale and the rest: "3D ship models" below.
- Message pictures are the game's scene pictures by message kind, not unit pictures; mods do not change them.

## 3D ship models (tactical battles)

Only **capital ships** (and Death Star classes) have 3D models. Fighters are flat camera-facing images in the original
(0.3 × 0.3 world units), not models; a `model` on a fighter is not used. The rules below follow the original's renderer
(`docs/spec/tactical.md` section 10); the glTF part is Open-SWR's own design.

- **Format:** glTF 2.0, `.glb` (one file, textures embedded) or `.gltf` with its `.bin` and textures beside it, inside the
  mod folder. Textures: PNG or JPEG.
- **Axes:** +y up, nose toward **−z**, the origin at the centre of the hull. No roll or pitch is applied in battle: capital
  ships stay level and turn around y only.
- **Scale:** 1 model unit = 1 battle unit, no scaling. Original ships range from about 0.6 (Corellian Corvette) through
  2.14 (Imperial Star Destroyer) to 8.0 long (Super Star Destroyer); weapon ranges are about 50 units, so ships are small
  next to their ranges. Keep your ship in the 0.3–8 range.
- **Collision size:** the game reads only the model's bounding box: half height = max(height / 2, 0.5) and size =
  max(√(width² + length²), 0.5). These set how ships keep apart and how fighters orbit them. No hardpoints or engine
  points are read: shots go from ship centre to ship centre.
- **Detail levels:** the originals have three meshes per ship (close, medium, far; textures 1024², 512², 256²). A mod may
  give one model; it is drawn at every distance.
- **Polygon count:** no limit is checked. The originals are low-poly; very heavy models may slow battles with many ships.
- If the model fails to load, the ship falls back to the game's own model (a plain box for a new ship class).

## Can Open-SWR run on mods alone, without the original game?

No. A mod layers changes over the original data; it cannot replace it. The game needs every original definition file and table
(17 definition files, 33 tables), the records the game code names directly (above), every mission type and the six major
characters, none of which a mod can create. The interface, texts, sounds, movies and advisor scripts also come from the
install. A mod-only game would need Open-SWR's own replacement for all of these — a separate project, not part of the loader.
