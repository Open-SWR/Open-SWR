using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenSWR.Sim.Ai;

/// <summary>
/// The computer controller's save: the original saves the whole AI (model with every record, all planners,
/// every operation and task, the strategy object). openswr writes every instance field of those objects: numbers, flags, lists,
/// sets, list stacks. Game objects are written as their SdRefs; records, operations and tasks once each
/// in a table (constructor key + fields) and referenced by their table index, so a record held by an op after its detach (0x302)
/// stays the same instance. Not written: back references (model, game, controller), delegates, the product catalogue (rebuilt at
/// load), the clone constants and the per-tick observation snapshot (retaken at load).
/// </summary>
static class AiSave
{
    static readonly Type[] Entities = [typeof(AiSystem), typeof(AiSector), typeof(AiAgent), typeof(AiFleet), typeof(AiShip),
        typeof(MissionOp), typeof(CharacterOp), typeof(FleetOp), typeof(FleetTask), typeof(GarrisonTask)];
    static bool IsEntity(Type t) => Array.IndexOf(Entities, t) >= 0;

    /// <summary>
    /// Fields the original does not save: product of planners 3, 6, 7, 10, 11, planner 14's walk cursor; the tree key
    /// counters, restarted at load) or always overwrites at load (the records' dirty flags): left out, so a load
    /// keeps their constructor value (dirty, none, -1) and a save right after a load equals the save it came from.
    /// </summary>
    static bool NotSaved(Type t, FieldInfo f) => f.Name switch
    {
        "<Dirty>k__BackingField" or "<NextOpKey>k__BackingField" or "<NextTaskKey>k__BackingField" or "<NextFleetOpKey>k__BackingField"
            or "<NextCharacterOpKey>k__BackingField" => true,
        "_fProduct" => t == typeof(DefencePlanner) || t == typeof(TroopPlanner) || t == typeof(SystemFighterPlanner) || t == typeof(FleetFighterPlanner) || t == typeof(CapitalShipPlanner),
        "_product" => t == typeof(TroopPlanner) || t == typeof(SystemFighterPlanner) || t == typeof(FleetFighterPlanner) || t == typeof(CapitalShipPlanner),
        "_cursor" => t == typeof(NamingPlanner),
        _ => false,
    };

    static bool Skipped(FieldInfo f) =>
        typeof(Delegate).IsAssignableFrom(f.FieldType) || f.FieldType == typeof(WorldModel) || f.FieldType == typeof(GameState)
        || f.FieldType == typeof(Catalogue) || f.FieldType == typeof(Clone) || f.FieldType == typeof(ComputerController)
        || f.Name is "_systemFields" or "_queues" or "_unitFields" or "_frozenContents" or "<IssueTrace>k__BackingField" or "<StepTrace>k__BackingField";

    static readonly Dictionary<Type, List<(string Name, FieldInfo Field)>> Layouts = [];

    /// <summary>Every saved instance field, base class first; a name repeated in a subclass is prefixed with its class.</summary>
    static List<(string Name, FieldInfo Field)> Layout(Type t)
    {
        lock (Layouts)
        {
            if (Layouts.TryGetValue(t, out var known)) return known;
            var chain = new List<Type>();
            for (var x = t; x is not null && x != typeof(object); x = x.BaseType) chain.Insert(0, x);
            var list = new List<(string, FieldInfo)>();
            foreach (var x in chain)
                foreach (var f in x.GetTypeInfo().DeclaredFields.Where(f => !f.IsStatic))
                {
                    if (Skipped(f) || NotSaved(t, f)) continue;
                    string name = f.Name.StartsWith('<') ? f.Name[1..f.Name.IndexOf('>')] : f.Name;
                    if (list.Any(e => e.Item1 == name)) name = x.Name + "." + name;
                    list.Add((name, f));
                }
            return Layouts[t] = list;
        }
    }

    static Type? DictionaryOf(Type t) => t.IsGenericType && t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>)) ? t : null;
    static Type? ElementOf(Type t) => t.IsArray ? t.GetElementType()
        : t.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GetGenericArguments()[0];

    /// <summary>Tag of a value written where the field type is <c>object</c> (ranked-list keys).</summary>
    static string Tag(object v) => v is GameObject ? "g" : v.GetType().Name;
    static readonly Dictionary<string, Type> Tags = new[] { typeof(int), typeof(uint), typeof(long), typeof(bool), typeof(string) }
        .Concat(Entities).ToDictionary(t => t.Name);

    internal sealed class Writer(Func<GameObject, uint> r)
    {
        readonly Dictionary<object, int> _ids = new(ReferenceEqualityComparer.Instance);
        readonly List<object> _order = [];

        int Id(object e)
        {
            if (!_ids.TryGetValue(e, out int id)) { _ids[e] = id = _order.Count; _order.Add(e); }
            return id;
        }

        /// <summary>The entity table; call last (writing an entry can add entries).</summary>
        public JsonArray Table()
        {
            var a = new JsonArray();
            for (int i = 0; i < _order.Count; i++)
                a.Add(new JsonObject { ["t"] = _order[i].GetType().Name, ["a"] = Args(_order[i]), ["f"] = Fields(_order[i]) });
            return a;
        }

        JsonArray Args(object e) => e switch
        {
            AiSystem x => [r(x.System)],
            AiSector x => [x.Sector.Definition.Common.Index],
            AiAgent x => [r(x.Agent)],
            AiFleet x => [r(x.Fleet)],
            AiShip x => [r(x.Ship)],
            MissionOp x => [x.Key, x.Class.Family, x.Class.Subtype, (int)x.Side],
            CharacterOp x => [x.Key, x.Type],
            FleetOp x => [x.Key],
            FleetTask x => [x.Type],
            GarrisonTask x => [x.Key],
            _ => throw new NotSupportedException(e.GetType().Name),
        };

        public JsonObject Fields(object o)
        {
            var j = new JsonObject();
            foreach (var (name, f) in Layout(o.GetType()))
            {
                var v = f.GetValue(o);
                j[name] = Value(v, f.FieldType);
            }
            return j;
        }

        JsonNode? Value(object? v, Type t)
        {
            if (v is null) return null;
            var u = Nullable.GetUnderlyingType(t) ?? t;
            if (u == typeof(object)) return new JsonArray(Tag(v), Value(v, v.GetType()));
            switch (v)
            {
                case Enum: return JsonValue.Create(Convert.ToInt64(v));
                case int x: return x;
                case uint x: return x;
                case long x: return x;
                case bool x: return x;
                case double x: return x;
                case string x: return x;
                case GameObject g: return r(g);
                case Sector x: return x.Definition.Common.Index;
                case OpClass c: return new JsonArray(c.Family, c.Subtype);
                case int[] w:
                    {
                        int n = w.Length;
                        while (n > 0 && w[n - 1] == 0) n--; // trailing zeros left out (the array keeps its constructor size)
                        var a = new JsonArray();
                        for (int i = 0; i < n; i++) a.Add(w[i]);
                        return a;
                    }
            }
            if (IsEntity(v.GetType())) return Id(v);
            if (v is ITuple tuple && u.IsValueType)
            {
                var args = u.GetGenericArguments();
                var a = new JsonArray();
                for (int i = 0; i < tuple.Length; i++) a.Add(Value(tuple[i], args[i]));
                return a;
            }
            if (DictionaryOf(u) is { } d)
            {
                var args = d.GetGenericArguments();
                var a = new JsonArray();
                foreach (DictionaryEntry e in (IDictionary)v) a.Add(new JsonArray(Value(e.Key, args[0]), Value(e.Value, args[1])));
                return a;
            }
            if (v is IEnumerable list && ElementOf(u) is { } el)
            {
                var a = new JsonArray();
                foreach (var x in list) a.Add(Value(x, el));
                return a;
            }
            if (u.Namespace == typeof(AiSave).Namespace && u.IsClass) return Fields(v); // owned helpers (ranked lists, list stacks)
            throw new NotSupportedException($"AI field type {u} cannot be saved.");
        }
    }

    internal sealed class Reader
    {
        readonly GameState _game;
        readonly Func<uint, GameObject> _o;
        readonly JsonArray _table;
        readonly object[] _entities;

        /// <summary>Creates every table entity through its constructor (fields are filled by <see cref="FillTable"/>).</summary>
        public Reader(GameState game, WorldModel model, Func<uint, GameObject> o, JsonArray table)
        {
            (_game, _o, _table) = (game, o, table);
            _entities = new object[table.Count];
            for (int i = 0; i < table.Count; i++)
            {
                var e = table[i]!;
                var a = e["a"]!.AsArray();
                uint U(int k) => a[k]!.GetValue<uint>();
                int I(int k) => a[k]!.GetValue<int>();
                _entities[i] = (string)e["t"]! switch
                {
                    nameof(AiSystem) => new AiSystem((StarSystem)o(U(0)), model),
                    nameof(AiSector) => new AiSector(Sector(U(0))),
                    nameof(AiAgent) => new AiAgent((Agent)o(U(0))),
                    nameof(AiFleet) => new AiFleet((Fleet)o(U(0))),
                    nameof(AiShip) => new AiShip(o(U(0))),
                    nameof(MissionOp) => new MissionOp(I(0), OpClass.Of(U(1), I(2)) ?? throw new InvalidDataException("unknown op class"), (Owner)I(3)),
                    nameof(CharacterOp) => new CharacterOp(I(0), I(1)),
                    nameof(FleetOp) => new FleetOp(I(0)),
                    nameof(FleetTask) => new FleetTask(I(0)),
                    nameof(GarrisonTask) => new GarrisonTask(I(0)),
                    var t => throw new InvalidDataException($"unknown AI entity {t}"),
                };
            }
        }

        Sector Sector(uint index) => _game.Sectors.First(s => s.Definition.Common.Index == index);

        public void FillTable()
        {
            for (int i = 0; i < _table.Count; i++) Fill(_entities[i], _table[i]!["f"]!.AsObject());
        }

        /// <summary>
        /// Overwrites every saved field; collections are refilled in place (their comparers kept). A field the save lacks keeps its
        /// constructor value (a planner class of another factory, a field added later).
        /// </summary>
        public void Fill(object o, JsonObject j)
        {
            foreach (var (name, f) in Layout(o.GetType()))
            {
                if (!j.TryGetPropertyValue(name, out var n)) continue;
                var current = f.GetValue(o);
                if (n is JsonArray a && current is int[] w)
                {
                    Array.Clear(w);
                    for (int i = 0; i < a.Count; i++) w[i] = a[i]!.GetValue<int>();
                }
                else if (n is JsonArray b && current is bool[] flags)
                {
                    Array.Clear(flags);
                    for (int i = 0; i < b.Count; i++) flags[i] = b[i]!.GetValue<bool>();
                }
                else if (n is JsonArray && current is not null && current is not ITuple && current is IEnumerable && f.FieldType != typeof(object))
                    Refill(current, n, f.FieldType);
                else f.SetValue(o, Read(n, f.FieldType));
            }
        }

        void Refill(object collection, JsonNode n, Type t)
        {
            var a = n.AsArray();
            if (DictionaryOf(collection.GetType()) is { } d)
            {
                var args = d.GetGenericArguments();
                var dict = (IDictionary)collection;
                dict.Clear();
                foreach (var e in a) dict.Add(Read(e![0], args[0])!, Read(e[1], args[1]));
                return;
            }
            var type = collection.GetType();
            var el = ElementOf(type)!;
            type.GetMethod("Clear")!.Invoke(collection, null);
            var add = type.GetMethod("Add", [el])!;
            foreach (var e in a) add.Invoke(collection, [Read(e, el)]);
        }

        object? Read(JsonNode? n, Type t)
        {
            if (n is null) return null;
            var u = Nullable.GetUnderlyingType(t) ?? t;
            if (u == typeof(object))
            {
                string tag = (string)n[0]!;
                return Read(n[1], tag == "g" ? typeof(GameObject) : Tags[tag]);
            }
            if (u.IsEnum) return Enum.ToObject(u, n.GetValue<long>());
            if (u.IsPrimitive || u == typeof(string)) return n.Deserialize(u);
            if (typeof(GameObject).IsAssignableFrom(u)) return _o(n.GetValue<uint>());
            if (u == typeof(Sector)) return Sector(n.GetValue<uint>());
            if (u == typeof(OpClass)) return OpClass.Of(n[0]!.GetValue<uint>(), n[1]!.GetValue<int>());
            if (IsEntity(u)) return _entities[n.GetValue<int>()];
            if (u.IsArray)
            {
                var a = n.AsArray();
                var arr = Array.CreateInstance(u.GetElementType()!, a.Count);
                for (int i = 0; i < a.Count; i++) arr.SetValue(Read(a[i], u.GetElementType()!), i);
                return arr;
            }
            if (u.IsValueType && typeof(ITuple).IsAssignableFrom(u))
            {
                var args = u.GetGenericArguments();
                return Activator.CreateInstance(u, [.. args.Select((x, i) => Read(n[i], x))]);
            }
            if (DictionaryOf(u) is not null || u.IsGenericType && ElementOf(u) is not null)
            {
                var c = Activator.CreateInstance(u)!;
                Refill(c, n, u);
                return c;
            }
            if (u.Namespace == typeof(AiSave).Namespace && u.IsClass)
            {
                var x = RuntimeHelpers.GetUninitializedObject(u);
                Fill(x, n.AsObject());
                return x;
            }
            throw new InvalidDataException($"AI field type {u} cannot be loaded.");
        }
    }
}
