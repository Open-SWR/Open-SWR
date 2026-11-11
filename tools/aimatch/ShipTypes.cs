using System.Globalization;
using System.Text;
using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using OpenSWR.Sim.Ai;

namespace OpenSWR.AiMatch;

/// <summary>One capital ship type of one side at the game's end: ships built during the game and ships alive at the end.</summary>
public sealed record ShipTypeCount(Owner Side, uint Type, int Built, int Alive, int Maintenance, string Role, int GravityWell, int Interdiction);

/// <summary>Capital ships by type: what each side built and kept, with the type's main role. An observer: reads the state, draws nothing.</summary>
public static class ShipTypes
{
    public static List<ShipTypeCount> Count(GameState game, int startObjects)
    {
        var result = new List<ShipTypeCount>();
        foreach (var side in new[] { Owner.Alliance, Owner.Empire })
        {
            var roles = Roles.Compute(game, Roles.SideTypes(game, side));
            var ships = game.Created.Skip(startObjects).Where(o => o.Owner == side && Family.IsCapitalShip(o.Type)).ToList();
            var alive = game.Created.Where(o => o.Owner == side && o.Existing && Family.IsCapitalShip(o.Type)).ToList();
            foreach (var type in ships.Select(o => o.Type).Concat(alive.Select(o => o.Type)).Distinct())
            {
                var r = roles.GetValueOrDefault(type);
                string role = r is null ? "?" : r.Interdictor ? "interdictor" : r[UnitRole.Bombard] > 0 ? $"{r.Main} bombard" : r.Main.ToString();
                var d = game.Definitions.Resolve(type);
                result.Add(new(side, type, ships.Count(o => o.Type == type), alive.Count(o => o.Type == type), game.MaintenanceCost(type), role,
                    d?.Get(SdField.GravityWell) ?? 0, d?.Get(SdField.InterdictionStrength) ?? 0));
            }
        }
        return result;
    }

    /// <summary>Per game averages by the side the OpenSWR AI played, for the openswr side and the original side.</summary>
    public static string Table(IReadOnlyList<GameResult> results)
    {
        var text = new StringBuilder();
        text.AppendLine("capital ships by type (mean per game: built during the game / alive at the end; maintenance each, main role, gravity well and interdiction strength)");
        foreach (var played in new[] { Owner.Alliance, Owner.Empire })
            foreach (bool openswr in new[] { true, false })
            {
                var games = results.Where(r => r.OpenSWR == played && r.ShipTypes is not null).ToList();
                if (games.Count == 0) continue;
                var side = openswr ? played : played == Owner.Alliance ? Owner.Empire : Owner.Alliance;
                var rows = games.SelectMany(r => r.ShipTypes!).Where(x => x.Side == side).GroupBy(x => x.Type)
                    .Select(g => (Type: g.Key, Built: g.Sum(x => x.Built) / (double)games.Count, Alive: g.Sum(x => x.Alive) / (double)games.Count, g.First().Maintenance, g.First().Role, g.First().GravityWell, g.First().Interdiction))
                    .OrderByDescending(x => x.Alive * x.Maintenance).ToList();
                text.AppendLine($"  {(openswr ? "openswr" : "original")} as {side} ({games.Count} games)");
                foreach (var x in rows)
                    text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                        $"    {Family.Of(x.Type)}:{SdRef.IndexOf(x.Type),-4} m{x.Maintenance,-3} {x.Role,-18} well {x.GravityWell,-4} interdiction {x.Interdiction,-4} built {x.Built,6:F2}  alive {x.Alive,6:F2}  alive maintenance {x.Alive * x.Maintenance,7:F0}"));
            }
        return text.ToString();
    }
}
