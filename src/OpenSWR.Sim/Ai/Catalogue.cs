using OpenSWR.Data.Dat;

namespace OpenSWR.Sim.Ai;

/// <summary>The product classes a pick or a list asks for.</summary>
public enum ProductFilter
{
    /// <summary>Any product.</summary>
    None,
    Shipyard, TrainingFacility, ConstructionYard, GenCore, DeathStarShield, Battery,
    /// <summary>Special forces of type 1 to 4 (types 5 to 8 count as 1 to 4).</summary>
    SpecialForceType1, SpecialForceType2, SpecialForceType3, SpecialForceType4,
    AttackTroop, DefenseTroop, Fighter, CapitalShip, Armed, AntiFighter, Bombarder, DeathStar,
    /// <summary>A capital ship that is a troop carrier.</summary>
    TroopCarrierShip,
    /// <summary>A capital ship that is armed.</summary>
    ArmedShip,
    /// <summary>A capital ship that is anti-fighter.</summary>
    AntiFighterShip,
    /// <summary>An anti-fighter capital ship with fighter capacity.</summary>
    FighterCarrierShip,
    /// <summary>A capital ship that can bombard.</summary>
    BombarderShip,
    /// <summary>A capital ship that is an interdictor.</summary>
    InterdictorShip,
}

/// <summary>Pick criterion: the value a product list ranks by.</summary>
public enum Criterion
{
    /// <summary>Nothing: every product ranks 0.</summary>
    None,
    MaintenanceCost,
    /// <summary>Troop and fighter detection (Inferred); capital ships rank their squadron size, 0. Meaning in the original is unknown.</summary>
    Detection,
    RefinedMaterialCost,
    /// <summary>Capital ships (fighter capacity).</summary>
    FighterCapacity,
    /// <summary>Capital ships (troop capacity).</summary>
    TroopCapacity,
    /// <summary>Fighters and capital ships (score vs ships). Meaning in the original is unknown.</summary>
    Firepower,
    /// <summary>Fighters and capital ships (score vs fighters). Meaning in the original is unknown.</summary>
    AntiFighterFirepower,
    /// <summary>Fighters and capital ships. Meaning in the original is unknown.</summary>
    Bombardment,
    /// <summary>Troops (AttackStrength).</summary>
    TroopAttack,
    /// <summary>Troops (DefenseStrength).</summary>
    TroopDefense,
    /// <summary>Production facilities (ProcessingRate).</summary>
    ProcessingRate,
    /// <summary>Fighters. Meaning in the original is unknown.</summary>
    Torpedoes,
    /// <summary>Defences (ShieldStrength), fighters and capital ships. Meaning in the original is unknown.</summary>
    Shields,
    /// <summary>Fighters (Maneuverability).</summary>
    Maneuverability,
    /// <summary>Defences (AttackStrength).</summary>
    DefenceAttack,
    /// <summary>Capital ships (10 * score vs ships div MaintenanceCost).</summary>
    StrengthPerMaintenance,
    Diplomacy,
    Espionage,
    Combat,
    Leadership,
}

/// <summary>
/// Product-catalogue record: one per SD unit type, keyed by its SdRef. The class fields mean
/// different things per class (table); the named accessors below read them for one class.
/// </summary>
public sealed class Product(SdRecord record)
{
    /// <summary>All fields 0: the reading of a type without a record.</summary>
    internal static readonly Product None = new(null!);

    public SdRecord Record { get; } = record;
    public uint Type => Record.Common.Ref;
    // class
    /// <summary>Capital ship, not an interdictor, some arc with turbolaser or ion.</summary>
    public bool Armed { get; set; }
    /// <summary>Capital ship, not an interdictor, no troop capacity, some arc with lasers.</summary>
    public bool AntiFighter { get; set; }
    /// <summary>A capital ship with troop capacity that is neither armed, anti-fighter nor an interdictor.</summary>
    public bool TroopCarrier { get; set; }
    /// <summary>An armed ship with troop capacity. Meaning in the original is unknown.</summary>
    public bool ArmedTroopCarrier { get; set; }
    /// <summary>Hull &lt; 1100.</summary>
    public bool SmallHull { get; set; }
    /// <summary>Hull &lt; 2000.</summary>
    public bool MediumHull { get; set; }
    public bool LargeHull { get; set; }
    /// <summary>The Death Star class.</summary>
    public bool DeathStar { get; set; }
    /// <summary>Capital ship with fighter capacity.</summary>
    public bool HasFighterCapacity { get; set; }
    /// <summary>Capital ship with troop capacity.</summary>
    public bool HasTroopCapacity { get; set; }
    public bool Interdictor { get; set; }
    /// <summary>Capital ship with a Bombardment value.</summary>
    public bool Bombarder { get; set; }
    /// <summary>Troop with DefenseStrength <= AttackStrength.</summary>
    public bool AttackTroop { get; set; }
    /// <summary>Troop with AttackStrength <= DefenseStrength.</summary>
    public bool DefenseTroop { get; set; }
    public bool Shipyard { get; set; }
    public bool TrainingFacility { get; set; }
    public bool ConstructionYard { get; set; }
    public bool GenCore { get; set; }
    public bool DeathStarShield { get; set; }
    /// <summary>KDY and LNR.</summary>
    public bool Battery { get; set; }
    /// <summary>Capital ship outside the Death Star class.</summary>
    public bool CapitalShip { get; set; }
    public bool Fighter { get; set; }
    /// <summary>Special forces: the type 1 to 4 (types 5 to 8 count as 1 to 4); 0 otherwise.</summary>
    public int SpecialForceKind { get; set; }

    /// <summary>The product is of the class the filter asks for.</summary>
    public bool Is(ProductFilter filter) => filter switch
    {
        ProductFilter.None => true,
        ProductFilter.Shipyard => Shipyard, ProductFilter.TrainingFacility => TrainingFacility, ProductFilter.ConstructionYard => ConstructionYard,
        ProductFilter.GenCore => GenCore, ProductFilter.DeathStarShield => DeathStarShield, ProductFilter.Battery => Battery,
        ProductFilter.SpecialForceType1 => SpecialForceKind == 1, ProductFilter.SpecialForceType2 => SpecialForceKind == 2,
        ProductFilter.SpecialForceType3 => SpecialForceKind == 3, ProductFilter.SpecialForceType4 => SpecialForceKind == 4,
        ProductFilter.AttackTroop => AttackTroop, ProductFilter.DefenseTroop => DefenseTroop, ProductFilter.Fighter => Fighter,
        ProductFilter.CapitalShip => CapitalShip, ProductFilter.Armed => Armed, ProductFilter.AntiFighter => AntiFighter, ProductFilter.Bombarder => Bombarder,
        ProductFilter.DeathStar => DeathStar, ProductFilter.TroopCarrierShip => CapitalShip && TroopCarrier, ProductFilter.ArmedShip => CapitalShip && Armed,
        ProductFilter.AntiFighterShip => CapitalShip && AntiFighter, ProductFilter.FighterCarrierShip => CapitalShip && HasFighterCapacity && AntiFighter,
        ProductFilter.BombarderShip => CapitalShip && Bombarder, ProductFilter.InterdictorShip => CapitalShip && Interdictor,
        _ => false,
    };
    public int RefinedMaterialCost { get; set; }
    public int MaintenanceCost { get; set; }
    public int ResearchOrder { get; set; }
    /// <summary>1 / 2 side flag, 0 both.</summary>
    public int Side { get; set; }
    /// <summary>Production facility ProcessingRate; defence / troop AttackStrength; fighter / capital-ship score vs ships; special-force Diplomacy. Meaning in the original is unknown.</summary>
    public int Firepower { get; set; }
    /// <summary>Defence ShieldStrength; troop DefenseStrength; fighter / capital-ship score vs fighters; special-force Espionage. Meaning in the original is unknown.</summary>
    public int AntiFighterFirepower { get; set; }
    /// <summary>Capital ship: 1 with a Bombardment value, else 0. Fighter: the SD value that is 2 on the bomber types. Meaning in the original is unknown.</summary>
    public int Bombardment { get; set; }
    /// <summary>Fighter: 25 * squadron div 10. Capital ship: 100 * ShieldRechargeRate. Meaning in the original is unknown.</summary>
    public int Shields { get; set; }
    /// <summary>Troop BombardmentDefense.</summary>
    public int BombardmentDefense { get; set; }
    /// <summary>Special-force Combat.</summary>
    public int Combat { get; set; }
    /// <summary>Special-force Leadership.</summary>
    public int Leadership { get; set; }
    /// <summary>Fighter Maneuverability.</summary>
    public int Maneuverability { get; set; }
    /// <summary>Troop and fighter SD value the OpenSWR AI reads as detection. Meaning in the original is unknown.</summary>
    public int Detection { get; set; }
    /// <summary>Fighter: the SD torpedo value times the squadron size (Inferred: torpedoes). Meaning in the original is unknown.</summary>
    public int Torpedoes { get; set; }
    /// <summary>Capital-ship Hull.</summary>
    public int Hull { get; set; }
    /// <summary>Capital-ship fighter capacity.</summary>
    public int FighterCapacity { get; set; }
    /// <summary>Capital-ship troop capacity.</summary>
    public int TroopCapacity { get; set; }
    /// <summary>Fighter squadron size.</summary>
    public int SquadronSize { get; set; }
    public int WeaponRechargeRate { get; set; }
    public int ShieldRechargeRate { get; set; }
    /// <summary>Capital ship: 10 * score vs ships div MaintenanceCost.</summary>
    public int StrengthPerMaintenance { get; set; }
    /// <summary>Capital ship: 10 * score vs ships div RefinedMaterialCost.</summary>
    public int StrengthPerCost { get; set; }

    // class readings of the fields above
    public int ProcessingRate => Firepower;
    public int DefenseStrength => AntiFighterFirepower;

    /// <summary>Criterion gives value, for the criteria the product's class ranks by.</summary>
    internal (Criterion Criterion, Func<Product, int> Value)[] Criteria { get; set; } = [];

    /// <summary>The value the pick ranks by for a criterion (0 when the class has none).</summary>
    public int Value(Criterion criterion)
    {
        foreach (var (c, value) in Criteria)
            if (c == criterion) return value(this);
        return 0;
    }
}

/// <summary>The catalogue: product records of every unit type in the SD data (Mines and Refineries have none).</summary>
public sealed class Catalogue
{
    readonly Dictionary<uint, Product> _products = [];
    public Product? this[uint type] => _products.GetValueOrDefault(type);
    /// <summary>The records in key order.</summary>
    public IEnumerable<Product> Products => _products.Values.OrderBy(p => p.Type);

    public Catalogue(GameDefinitions definitions)
    {
        foreach (var r in definitions.SdFiles.SelectMany(f => f.Records))
            if (Build(r, definitions.SpecialForces) is { } p) _products[p.Type] = p;
    }

    static int Maintenance(Product p) => p.MaintenanceCost;
    static int Refined(Product p) => p.RefinedMaterialCost;

    static Product? Build(SdRecord r, List<SpecialForceRecord> forces)
    {
        uint family = Family.Of(r.Common.Ref);
        bool facility = family is >= Families.IonCannon and < Families.Mine;
        if (!(facility || family is >= Families.Troop and < Families.Headquarters || family is >= Families.SpecialForce and < Families.Mission) || r.Body.Length < 6) return null;
        int I(SdField field) => r.GetOrZero(field);
        var p = new Product(r) { RefinedMaterialCost = I(SdField.RefinedMaterialCost), MaintenanceCost = I(SdField.MaintenanceCost), ResearchOrder = I(SdField.ResearchOrder) };
        p.Side = facility ? I(SdField.Empire) == 0 ? 1 : I(SdField.Alliance) == 0 ? 2 : 0 : I(SdField.Empire) != 0 ? 2 : 1;
        switch (family)
        {
            case >= Families.Shipyard and < Families.Mine: // production facilities
                p.Firepower = I(SdField.ProcessingRate);
                (p.Shipyard, p.TrainingFacility, p.ConstructionYard) = (family == Families.Shipyard, family == Families.TrainingFacility, family == Families.ConstructionYard);
                p.Criteria = [(Criterion.ProcessingRate, x => x.Firepower), (Criterion.MaintenanceCost, Maintenance), (Criterion.RefinedMaterialCost, Refined)];
                break;
            case >= Families.IonCannon and < Families.Shipyard: // defences
                (p.Firepower, p.AntiFighterFirepower) = (I(SdField.DefenseAttackStrength), I(SdField.DefenseShieldStrength));
                (p.GenCore, p.DeathStarShield, p.Battery) = (family == Families.PlanetaryShield, family == Families.DeathStarShield, family is Families.IonCannon or Families.Gun);
                p.Criteria = [(Criterion.Shields, x => x.AntiFighterFirepower), (Criterion.DefenceAttack, x => x.Firepower), (Criterion.MaintenanceCost, Maintenance),
                    (Criterion.RefinedMaterialCost, Refined)];
                break;
            case >= Families.Troop and < Families.CapitalShip: // troops
                (p.Firepower, p.AntiFighterFirepower, p.BombardmentDefense, p.Detection) = (I(SdField.TroopAttackStrength), I(SdField.TroopDefenseStrength), I(SdField.TroopBombardmentDefense), I(SdField.Detection));
                p.AttackTroop = p.AntiFighterFirepower <= p.Firepower;
                p.DefenseTroop = p.Firepower <= p.AntiFighterFirepower;
                p.Criteria = [(Criterion.TroopAttack, x => x.Firepower), (Criterion.TroopDefense, x => x.AntiFighterFirepower), (Criterion.MaintenanceCost, Maintenance),
                    (Criterion.Detection, x => x.Detection), (Criterion.RefinedMaterialCost, Refined)];
                break;
            case >= Families.Fighter and < Families.Headquarters: // fighters
            {
                int squad = I(SdField.SquadronSize);
                (p.SquadronSize, p.Shields, p.Maneuverability, p.Bombardment, p.Detection) = (squad, 25 * squad / 10, I(SdField.Maneuverability), I(SdField.FighterBombardment), I(SdField.Detection));
                p.Torpedoes = I(SdField.Torpedoes) * squad;
                int ion = I(SdField.ForeIonCannon), laser = I(SdField.ForeLaserCannon);
                if (laser + ion != 0 && (100 * ion + 100 * laser / 6) * squad / (laser + ion) is > 0 and var fire) p.Firepower = fire;
                if (laser > 0)
                {
                    int t = laser * p.Maneuverability * squad * 10 / laser;
                    p.AntiFighterFirepower = t + I(SdField.ShieldStrength) * t / 8;
                }
                p.Fighter = true;
                p.Criteria = [(Criterion.Firepower, x => x.Firepower), (Criterion.AntiFighterFirepower, x => x.AntiFighterFirepower), (Criterion.Bombardment, x => x.Bombardment),
                    (Criterion.MaintenanceCost, Maintenance), (Criterion.Detection, x => x.Detection), (Criterion.RefinedMaterialCost, Refined),
                    (Criterion.Shields, x => x.Shields), (Criterion.Torpedoes, x => x.Torpedoes), (Criterion.Maneuverability, x => x.Maneuverability)];
                break;
            }
            case >= Families.CapitalShip and < Families.Fighter: // capital ships
            {
                (p.Hull, p.FighterCapacity, p.TroopCapacity, p.WeaponRechargeRate, p.ShieldRechargeRate) = (I(SdField.Hull), I(SdField.FighterCapacity),
                    I(SdField.TroopCapacity), I(SdField.WeaponRechargeRate), I(SdField.ShieldRechargeRate));
                p.Bombardment = I(SdField.Bombardment) != 0 ? 1 : 0;
                p.Shields = 100 * I(SdField.ShieldRechargeRate);
                bool interdictor = I(SdField.GravityWell) > 0;
                if (family < Families.DeathStar) // weapon scores, not for the Death Star class; arcs fore, aft, port, starboard
                {
                    (int Turbolaser, int Ion, int Laser)[] arcs =
                    [
                        (I(SdField.ForeTurbolaser), I(SdField.ForeIonCannon), I(SdField.ForeLaserCannon)),
                        (I(SdField.AftTurbolaser), I(SdField.AftIonCannon), I(SdField.AftLaserCannon)),
                        (I(SdField.PortTurbolaser), I(SdField.PortIonCannon), I(SdField.PortLaserCannon)),
                        (I(SdField.StarboardTurbolaser), I(SdField.StarboardIonCannon), I(SdField.StarboardLaserCannon)),
                    ];
                    (int Turbolaser, int Ion, int Laser) Best(Func<(int, int, int), int> score)
                    {
                        int m = 0;
                        for (int k = 1; k < 4; k++) if (score(arcs[k]) > score(arcs[m])) m = k;
                        return arcs[m];
                    }
                    static int V((int Turbolaser, int Ion, int Laser) a) => 100 * a.Turbolaser + 100 * a.Ion + 100 * a.Laser / 6;
                    static int W((int Turbolaser, int Ion, int Laser) a) => 10 * a.Laser + 10 * a.Turbolaser / 2;
                    var (mv, mw) = (Best(V), Best(W));
                    if (V(mv) > 0) p.Firepower = I(SdField.WeaponRechargeRate) * V(mv) / (mv.Turbolaser + mv.Ion + mv.Laser);
                    if (W(mw) > 0) p.AntiFighterFirepower = I(SdField.Maneuverability) * I(SdField.WeaponRechargeRate) * W(mw) / (mw.Turbolaser + mw.Laser);
                    if (!interdictor && arcs.Any(a => 100 * a.Turbolaser + 100 * a.Ion > 0)) p.Armed = true;
                    if (!interdictor && p.TroopCapacity == 0 && arcs.Any(a => a.Laser > 0)) p.AntiFighter = true;
                }
                if (p.MaintenanceCost != 0) p.StrengthPerMaintenance = 10 * p.Firepower / p.MaintenanceCost;
                if (p.RefinedMaterialCost != 0) p.StrengthPerCost = 10 * p.Firepower / p.RefinedMaterialCost;
                if (family >= Families.DeathStar) p.DeathStar = true;
                else
                {
                    p.CapitalShip = true;
                    if (interdictor) p.Interdictor = true;
                    (p.SmallHull, p.MediumHull, p.LargeHull) = (p.Hull < 1100, p.Hull is >= 1100 and < 2000, p.Hull >= 2000);
                }
                if (p.TroopCapacity != 0)
                {
                    p.HasTroopCapacity = true;
                    if (p.CapitalShip && !p.Armed && !p.AntiFighter && !p.Interdictor) p.TroopCarrier = true;
                    if (p.Armed) p.ArmedTroopCarrier = true;
                }
                if (p.FighterCapacity != 0) p.HasFighterCapacity = true;
                if (p.Bombardment != 0) p.Bombarder = true;
                p.Criteria = [(Criterion.Firepower, x => x.Firepower), (Criterion.StrengthPerMaintenance, x => x.StrengthPerMaintenance),
                    (Criterion.AntiFighterFirepower, x => x.AntiFighterFirepower), (Criterion.Bombardment, x => x.Bombardment), (Criterion.FighterCapacity, x => x.FighterCapacity),
                    (Criterion.TroopCapacity, x => x.TroopCapacity), (Criterion.MaintenanceCost, Maintenance), (Criterion.Detection, x => x.SquadronSize),
                    (Criterion.RefinedMaterialCost, Refined), (Criterion.Shields, x => x.Shields)];
                break;
            }
            default: // special forces
                var skills = forces.FirstOrDefault(x => x.Common.Ref == r.Common.Ref)?.Skills ?? default;
                (p.Firepower, p.AntiFighterFirepower, p.Combat, p.Leadership) = ((int)skills.Diplomacy.Base, (int)skills.Espionage.Base, (int)skills.Combat.Base, (int)skills.Leadership.Base);
                p.SpecialForceKind = (int)((r.Common.Index - 1) % 4) + 1;
                p.Criteria = [(Criterion.MaintenanceCost, Maintenance), (Criterion.RefinedMaterialCost, Refined), (Criterion.Diplomacy, x => x.Firepower),
                    (Criterion.Espionage, x => x.AntiFighterFirepower), (Criterion.Combat, x => x.Combat), (Criterion.Leadership, x => x.Leadership)];
                break;
        }
        return p;
    }
}
