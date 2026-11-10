using OpenSWR.Data.Dat;

namespace OpenSWR.Sim.Ai;

public sealed partial class WorldModel
{
    /// <summary>
    /// Product pick: the side's buildable list for the producer family in its order, filtered by the class
    /// mask; criterion 0 gives one rand(count) (count gives none), else the ranked list (dir) by the criterion value and the pick.
    /// Returns the product and its MaintenanceCost (0 when none).
    /// </summary>
    public (Product? Product, int Cost) PickProduct(uint producer, ProductFilter filter, Criterion criterion, RankOrder dir)
    {
        var candidates = Research.Buildable(_game, Side, producer).Select(r => Catalogue[r.Common.Ref])
            .Where(p => p is not null && p.Is(filter)).Cast<Product>().ToList();
        Product? chosen;
        if (criterion == Criterion.None)
        {
            if (candidates.Count == 0) return (null, 0);
            int i = Rand(candidates.Count);
            chosen = i < candidates.Count ? candidates[i] : null;
        }
        else
        {
            var stack = new ListStack();
            stack.Push(new RankedList(candidates.Select(p => ((object)p, p.Value(criterion))), dir, Rand));
            chosen = stack.Pick() as Product;
        }
        return (chosen, chosen?.MaintenanceCost ?? 0);
    }

    /// <summary>The advisor model's product pick (the same pick over the advisor's catalogue); no model state, no init draws.</summary>
    internal static (Product? Product, int Cost) PickProduct(GameState game, Owner side, Catalogue catalogue, Func<int, int> rand, uint producer, ProductFilter filter, Criterion criterion, RankOrder dir) =>
        new WorldModel(side) { _game = game, Catalogue = catalogue, Rand = rand }.PickProduct(producer, filter, criterion, dir);

    /// <summary>cap - dem &lt; budget gives 0 (both budgets).</summary>
    int Clamp(int budget) => MaintenanceCapacity - MaintenanceDemand < budget ? 0 : budget;

    /// <summary>Facility budget: pct(cap, p)*g div 100 - sum the field over the planner's systems.</summary>
    public int FacilityBudget(int p, int g, Func<AiSystem, int> field, IEnumerable<StarSystem> systems) =>
        Clamp(GameMath.Pct(MaintenanceCapacity, p) * g / 100 - systems.Sum(s => field(Systems[s])));

    /// <summary>Unit budget: (p*cap div 100)*f div 100 - U.</summary>
    public int UnitBudget(int p, int f, int used) => Clamp(p * MaintenanceCapacity / 100 * f / 100 - used);
}
