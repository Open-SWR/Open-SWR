using OpenSWR.Data.Dat;

namespace OpenSWR.Sim.Ai;

public sealed partial class WorldModel
{
    /// <summary>
    /// Init: the largest ResearchOrder of the side's ships + fighters / troops / all defence and production
    /// facilities, the side's minor characters (into the model's minor-character count), the research levels, then the six
    /// ship-line bests and the troop best. Inferred: the order of the seven lists (ship line first) and the minor-character side
    /// flag (SD Alliance / Empire flags).
    /// </summary>
    void Summary(GameState game, (int Ship, int Troop, int Facility) levels = default)
    {
        var products = Catalogue.Products.ToList();
        int MaxOrder(Func<Product, bool> f) => products.Where(f).Select(p => p.ResearchOrder).DefaultIfEmpty(0).Max();
        bool Own(Product p) => p.Side == (int)Side;
        MaxShipOrder = MaxOrder(p => Own(p) && Family.Of(p.Type) is >= Families.CapitalShip and < Families.Headquarters);
        MaxTroopOrder = MaxOrder(p => Own(p) && Family.IsTroop(p.Type));
        MaxFacilityOrder = MaxOrder(p => Family.Of(p.Type) is >= Families.IonCannon and < Families.Mine);
        MinorCharacters = game.Definitions.MinorCharacters.Count(c => (Side == Owner.Alliance ? c.Alliance : c.Empire) != 0);
        // init: the catalogue constructor zeroes the levels and the lists run at level 0; the research messages
        // (Observe) bring them up to the side's levels later. Load: the saved levels.
        (ShipLevel, TroopLevel, FacilityLevel) = levels;
        ShipBests();
        TroopBest();
    }

    /// <summary>
    /// The advisor's model init (on its own catalogue): the same seven catalogue lists
    /// for <paramref name="side"/> on the UI generator; the results are unused.
    /// </summary>
    public static void CatalogueLists(GameState game, Owner side, Func<int, int> rand)
    {
        var advisor = new WorldModel(side) { Rand = rand, Catalogue = new Catalogue(game.Definitions) };
        advisor.Summary(game);
    }

    /// <summary>Ranked list (dir Ascending) of the side's records with the mask and ResearchOrder <= level by the criterion; the best one's value.</summary>
    int Best(int level, ProductFilter filter, Criterion criterion)
    {
        var stack = new ListStack();
        stack.Push(new RankedList(Catalogue.Products.Where(p => p.Side == (int)Side && p.Is(filter) && p.ResearchOrder <= level)
            .Select(p => ((object)p, p.Value(criterion))), RankOrder.Ascending, Rand));
        return stack.Pick() is Product best ? best.Value(criterion) : 0;
    }

    void ShipBests()
    {
        int level = ShipLevel;
        BestFighterAntiFighterFirepower = Best(level, ProductFilter.Fighter, Criterion.AntiFighterFirepower);
        BestArmedFirepower = Best(level, ProductFilter.Armed, Criterion.Firepower);
        BestAntiFighterFirepower = Best(level, ProductFilter.AntiFighter, Criterion.AntiFighterFirepower);
        BestFighterCapacity = Best(level, ProductFilter.CapitalShip, Criterion.FighterCapacity);
        BestTroopCapacity = Best(level, ProductFilter.CapitalShip, Criterion.TroopCapacity);
        BestBombardment = Best(level, ProductFilter.Bombarder, Criterion.Bombardment);
    }

    void TroopBest() => BestTroopDefense = Best(TroopLevel, ProductFilter.DefenseTroop, Criterion.TroopDefense);

    // ---- load ----

    void SectorAggregates()
    {
        // zero every sector aggregate
        (OwnSystems, EnemySystems, NeutralSystems, OwnSectors, EnemySectors, NeutralSectors, SectorsWithoutKey, SectorsNotHeld, TroopSurplus) = (0, 0, 0, 0, 0, 0, 0, 0, 0);
        (Mines, Refineries, TroopMaintenance, DefenceMaintenance, FighterMaintenance, FreeEnergy, FreeRawMaterial, EnemyUsedEnergy) = (0, 0, 0, 0, 0, 0, 0, 0);
        (ProducerMaintenance, EnemyProducerMaintenance, GarrisonTarget, Troops, TheaterBalance, MaxEnemyFighters, MaxEnemyTroopsToTake) = (0, 0, 0, 0, 0, 0, 0);
        (MinEnemyTroopsToTake, DiplomacyTargets, EnemySupported, UprisingNoFleet, LowSupportHubs, UnblockadedKeySystems) = (0, 0, 0, 0, 0, 0);
        AssaultBase = DefenseBase = OwnCoruscant = OwnLowSupport = Headquarters = false;
        int smallest = 0;
        foreach (var x in Sectors)
        {
            OwnSystems += x.OwnSystems;
            EnemySystems += x.EnemySystems;
            NeutralSystems += x.NeutralSystems;
            Mines += x.Mines;
            Refineries += x.Refineries;
            TroopMaintenance += x.TroopMaintenance;
            DefenceMaintenance += x.DefenceMaintenance;
            FighterMaintenance += x.FighterMaintenance;
            FreeEnergy += x.FreeEnergy;
            FreeRawMaterial += x.FreeRawMaterial;
            EnemyUsedEnergy += x.EnemyUsedEnergy;
            ProducerMaintenance += x.ProducerMaintenance;
            EnemyProducerMaintenance += x.EnemyProducerMaintenance;
            TroopSurplus += x.TroopSurplus;
            GarrisonTarget += x.GarrisonTarget;
            Troops += x.Troops;
            TheaterBalance += x.TheaterBalance;
            UprisingNoFleet += x.UprisingNoFleet;
            LowSupportHubs += x.LowSupportHubs;
            UnblockadedKeySystems += x.UnblockadedKeySystems;
            DiplomacyTargets += x.DiplomacyTargets;
            EnemySupported += x.EnemySupported;
            MaxEnemyTroopsToTake = Math.Max(MaxEnemyTroopsToTake, x.MaxEnemyTroopsToTake);
            MaxEnemyFighters = Math.Max(MaxEnemyFighters, x.MaxEnemyFighters);
            MaxEnemyUnits = Math.Max(MaxEnemyUnits, x.MaxEnemyUnits);
            if (x.Own)
            {
                OwnSectors++;
                if (x.AssaultBase) AssaultBase = true;
                if (x.DefenseBase) DefenseBase = true;
                if (x.OwnCoruscant) OwnCoruscant = true;
                if (x.OwnLowSupport) OwnLowSupport = true;
            }
            if (x.Enemy)
            {
                EnemySectors++;
                if (x.EnemyTroopsToTake != 0 && (smallest == 0 || x.EnemyTroopsToTake < smallest)) smallest = x.EnemyTroopsToTake;
            }
            if (x.Neutral) NeutralSectors++;
            if (!x.KeySystem) SectorsWithoutKey++;
            if (!x.AllHeld) SectorsNotHeld++;
            if (x.Headquarters) Headquarters = true;
        }
        MinEnemyTroopsToTake = smallest;
        if (DiplomacyTargets > 0) DiplomacyTargets = DiplomacyTargets / 5 + 1;
        if (EnemySupported > 0) EnemySupported = EnemySupported / 6 + 1;
    }
}
