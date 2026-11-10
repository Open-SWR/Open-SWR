using OpenSWR.Data;
using OpenSWR.Data.Dat;

namespace OpenSWR.Tests;

public class SdFileTests
{
    static byte[] Dat(string name) => File.ReadAllBytes(GameDir.File(Path.Combine("GData", name)));

    [GameFact]
    public void AllSdFilesHaveFixedSizeRecords()
    {
        var files = Directory.GetFiles(GameDir.File("GData"), "*SD.DAT");
        Assert.Equal(18, files.Length);
        foreach (var path in files)
        {
            var (header, records) = SdRecord.Read(File.ReadAllBytes(path));
            Assert.Equal(1u, header.Unk00);
            Assert.All(records, r => Assert.True(r.Common.Family == 0 || (r.Common.Family >= header.FamilyMin && r.Common.Family < header.FamilyMax)));
        }
    }

    [GameFact]
    public void GenericRecordsMatchGame()
    {
        var (_, ships) = SdRecord.Read(Dat("CAPSHPSD.DAT"));
        var (_, chars) = SdRecord.Read(Dat("MJCHARSD.DAT"));
        Assert.Equal(30, ships.Count);
        Assert.Equal(44, ships[0].Body.Length);
        Assert.Equal(6, chars.Count);
        // Side flags at 24 / 28: Alliance-only, Empire-only (Inferred)
        Assert.Equal((1u, 0u), (ships[0].U32At(24), ships[0].U32At(28)));
        Assert.Equal((0u, 1u), (chars[^1].U32At(24), chars[^1].U32At(28)));
        Assert.Equal((ushort)0x2881, chars[^1].Common.StringId);
        Assert.All(chars, c => Assert.Equal(2, c.Common.StringModule));
    }

    [GameFact]
    public void SectorsAndSystemsMatchGame()
    {
        var sectors = SectorRecord.Read(Dat("SECTORSD.DAT"));
        var systems = SystemRecord.Read(Dat("SYSTEMSD.DAT"));
        var names = PeResources.Load(GameDir.File("TEXTSTRA.DLL")).OfType(ResId.String)
            .SelectMany(b => StringTable.Decode(b.Name.Id, b.Data.Span)).ToDictionary(s => s.Id, s => s.Text);

        Assert.Equal(20, sectors.Count);
        Assert.Equal(200, systems.Count);
        Assert.All(sectors, s => Assert.True(names.ContainsKey(s.Common.StringId)));
        Assert.All(systems, s => Assert.True(names.ContainsKey(s.Common.StringId)));

        var sectorByIndex = sectors.ToDictionary(s => s.Common.Index);
        Assert.All(systems.GroupBy(s => s.SectorIndex), g => Assert.Equal(10, g.Count()));
        Assert.All(systems, s => Assert.Equal(s.Common.Family == 0x90, sectorByIndex[s.SectorIndex].Group == 1));

        var coruscant = systems.Single(s => names[s.Common.StringId] == "Coruscant");
        Assert.Equal("Core Worlds North", names[sectorByIndex[coruscant.SectorIndex].Common.StringId]);
        Assert.Equal((423, 392), ((int)coruscant.X, (int)coruscant.Y));
    }

    [GameFact]
    public void CapitalShipsMatchEncyclopedia()
    {
        var ships = CapitalShipRecord.Read(Dat("CAPSHPSD.DAT"));
        Assert.Equal(30, ships.Count);
        foreach (var s in ships)
        {
            var Stat = EncyStats(s.Common);
            Assert.Equal(Stat(@"Cost (\d+)"), (int)s.RefinedMaterialCost);
            Assert.Equal(Stat(@"Maintenance (\d+)"), (int)s.MaintenanceCost);
            Assert.Equal(Stat(@"Fighters (\d+)"), (int)s.FighterCapacity);
            Assert.Equal(Stat(@"Troops (\d+)"), (int)s.TroopCapacity);
            Assert.Equal(Stat(@"Shield/Recharge (\d+)"), (int)s.ShieldStrength);
            Assert.Equal(Stat(@"Shield/Recharge \d+/(\d+)"), (int)s.ShieldRechargeRate);
            Assert.Equal(Stat(@"Hull (\d+)"), (int)s.Hull);
            Assert.Equal(Stat(@"Maneuverability (\d+)"), (int)s.Maneuverability);
            Assert.Equal(Stat(@"Hyperdrive/Backup (\d+)"), (int)s.Hyperdrive);
            Assert.Equal(Stat(@"Bombardment (\d+)"), (int)s.Bombardment);

            uint Sum(Func<Weapons, uint> w) => w(s.Fore) + w(s.Aft) + w(s.Port) + w(s.Starboard);
            Assert.Equal(new Weapons(Sum(w => w.Turbolaser), Sum(w => w.IonCannon), Sum(w => w.LaserCannon)), s.AttackStrength);
            Assert.Equal(s.AttackStrength.Turbolaser + s.AttackStrength.IonCannon + s.AttackStrength.LaserCannon, s.TotalAttackStrength);
        }
        var mc80 = ships[0];
        Assert.Equal((new Weapons(60, 40, 0), new Weapons(120, 60, 0), new Weapons(50, 35, 0)), (mc80.Fore, mc80.Port, mc80.Range));
        Assert.Equal((50u, 18u, 100u), (mc80.DamageControl, mc80.WeaponRechargeRate, mc80.HyperdriveIfDamaged));
    }

    [GameFact]
    public void FightersMatchEncyclopedia()
    {
        var fighters = FighterRecord.Read(Dat("FIGHTSD.DAT"));
        Assert.Equal(8, fighters.Count);
        foreach (var f in fighters)
        {
            var Stat = EncyStats(f.Common);
            Assert.Equal(Stat(@"Refined Material Cost:\s+(\d+)"), (int)f.RefinedMaterialCost);
            Assert.Equal(Stat(@"Maintenance Cost:\s+(\d+)"), (int)f.MaintenanceCost);
            Assert.Equal(Stat(@"\(Laser Cannon\):\s+(\d+)"), (int)f.AttackStrength.LaserCannon);
            Assert.Equal(Stat(@"\(Ion Cannon\):\s+(\d+)"), (int)f.AttackStrength.IonCannon);
            Assert.Equal(Stat(@"Shield Energy:\s+(\d+)"), (int)f.ShieldStrength);
            Assert.Equal(Stat(@"Hyperdrive:\s+(Yes)") == 1, f.Hyperdrive > 0);
            Assert.Equal(f.AttackStrength.Turbolaser + f.AttackStrength.IonCannon + f.AttackStrength.LaserCannon, f.TotalAttackStrength);
        }
    }

    [GameFact]
    public void TroopsMatchEncyclopedia()
    {
        var troops = TroopRecord.Read(Dat("TROOPSD.DAT"));
        Assert.Equal(10, troops.Count);
        foreach (var t in troops)
        {
            var Stat = EncyStats(t.Common);
            Assert.Equal(Stat(@"Refined Material Cost:\s+(\d+)"), (int)t.RefinedMaterialCost);
            Assert.Equal(Stat(@"Maintenance Cost:\s+(\d+)"), (int)t.MaintenanceCost);
            Assert.Equal(Stat(@"Attack Strength:\s+(\d+)"), (int)t.AttackStrength);
            Assert.Equal(Stat(@"Defense Strength:\s+(\d+)"), (int)t.DefenseStrength);
            Assert.Equal(Stat(@"Bombardment Defense:\s+(\d+)"), (int)t.BombardmentDefense);
        }
    }

    [GameFact]
    public void FacilitiesMatchEncyclopedia()
    {
        var facilities = new[] { "MANFACSD.DAT", "PROFACSD.DAT", "ALLFACSD.DAT" }.SelectMany(f => FacilityRecord.Read(Dat(f))).ToList();
        var defenses = DefenseFacilityRecord.Read(Dat("DEFFACSD.DAT"));
        Assert.Equal(9, facilities.Count);
        Assert.Equal(6, defenses.Count);
        Assert.Null(facilities[^1].ProcessingRate); // ALLFACSD: 52-byte records

        var withEncy = facilities.Select(f => (f.Common, f.RefinedMaterialCost, f.MaintenanceCost, f.BombardmentDefense))
            .Concat(defenses.Select(d => (d.Common, d.RefinedMaterialCost, d.MaintenanceCost, d.BombardmentDefense)))
            .Where(f => HasEncy(f.Common)).ToList();
        Assert.Equal(14, withEncy.Count); // all but Alliance Headquarters
        foreach (var (c, cost, maintenance, bombardmentDefense) in withEncy)
        {
            var Stat = EncyStats(c);
            Assert.Equal(Stat(@"Refined Material Cost:\s+(\d+)"), (int)cost);
            Assert.Equal(Stat(@"Maintenance Cost:\s+(\d+)"), (int)maintenance);
            Assert.Equal(Stat(@"Bombardment Defense:\s+(\d+)"), (int)bombardmentDefense);
        }
        foreach (var d in defenses)
        {
            var Stat = EncyStats(d.Common);
            Assert.Equal(Stat(@"Attack Strength:\s+(\d+)"), (int)d.AttackStrength);
            Assert.Equal(Stat(@"Shield Strength:\s+(\d+)"), (int)d.ShieldStrength);
        }
    }

    [GameFact]
    public void SpecialForcesAndMissionsMatchGame()
    {
        var forces = SpecialForceRecord.Read(Dat("SPECFCSD.DAT"));
        var missions = MissionRecord.Read(Dat("MISSNSD.DAT"));
        Assert.Equal(9, forces.Count);
        Assert.Equal(25, missions.Count);
        foreach (var f in forces.Where(f => HasEncy(f.Common)))
        {
            var Stat = EncyStats(f.Common);
            Assert.Equal(Stat(@"Refined Material Cost:\s+(\d+)"), (int)f.RefinedMaterialCost);
            Assert.Equal(Stat(@"Maintenance Cost:\s+(\d+)"), (int)f.MaintenanceCost);
        }

        // Each special force has one mask bit; mission masks are unions of them (Inferred meaning)
        Assert.All(forces, f => Assert.True(System.Numerics.BitOperations.IsPow2(f.MissionMask)));
        uint all = forces.Aggregate(0u, (m, f) => m | f.MissionMask);
        Assert.All(missions, m => Assert.Equal(0u, m.SpecialForceMask & ~all));
        uint Mask(params int[] forceIndices) => forceIndices.Aggregate(0u, (m, i) => m | forces.Single(f => f.Common.Index == (uint)i).MissionMask);
        Assert.Equal(Mask(4, 6), missions.Single(m => m.Common.Index == 0x13).SpecialForceMask); // Espionage: Bothan Spies, Imperial Espionage Droid
        Assert.Equal(Mask(3, 5), missions.Single(m => m.Common.Index == 0x15).SpecialForceMask); // Reconnaissance: Longprobe Y-wing, Imperial Probe Droid
    }

    [GameFact]
    public void CharactersMatchGame()
    {
        var major = CharacterRecord.Read(Dat("MJCHARSD.DAT"));
        var minor = CharacterRecord.Read(Dat("MNCHARSD.DAT"));
        Assert.Equal(6, major.Count);
        Assert.Equal(54, minor.Count);
        Assert.All(major.Concat(minor), c => Assert.Equal(1u, c.Alliance ^ c.Empire));
        Assert.All(major.Concat(minor), c => Assert.Equal(100u, c.Skills.Loyalty.Base));

        var luke = major.Single(c => c.Common.StringId == 0x2842);
        Assert.Equal((1u, 1u, new Skill(50, 0)), (luke.IsKnownJedi, luke.IsJediTrainer, luke.JediLevel));
        var veers = minor.Single(c => c.Common.StringId == 0x2a83); // General only
        Assert.Equal((0u, 0u, 1u), (veers.CanBeAdmiral, veers.CanBeCommander, veers.CanBeGeneral));
    }

    static PeResources? _ency;

    static bool HasEncy(SdCommon c) =>
        (_ency ??= PeResources.Load(GameDir.File("ENCYTEXT.DLL"))).Find(ResId.RcData, (ushort)(c.StringId - 0x1000)) != null;

    /// <summary>Stat lookup in the entity's encyclopedia entry: id = name string id - 0x1000 (25th Anniversary mod text carries stat blocks).
    /// Returns group 1 as int, 1 for a non-numeric match, 0 for no match.</summary>
    static Func<string, int> EncyStats(SdCommon c)
    {
        _ency ??= PeResources.Load(GameDir.File("ENCYTEXT.DLL"));
        var text = System.Text.Encoding.Latin1.GetString(_ency.Find(ResId.RcData, (ushort)(c.StringId - 0x1000))!.Data.Span);
        return pattern =>
        {
            var m = System.Text.RegularExpressions.Regex.Match(text, pattern);
            return !m.Success ? 0 : int.TryParse(m.Groups[1].Value, out var v) ? v : 1;
        };
    }
}
