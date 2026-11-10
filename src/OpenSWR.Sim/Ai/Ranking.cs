namespace OpenSWR.Sim.Ai;

/// <summary>
/// Ranked list: candidates inserted one at a time in candidate order in front of
/// the first entry X comes before; before(X, Y) is X.value &lt; Y.value with dir = 1, X.value &gt; Y.value with dir != 1, and on equal values
/// one UI draw, rand(10) &lt; 5. Then ranks 1, 2, ... in list order. dir 1 sorts ascending (the pick prefers large values), dir 2 descending.
/// </summary>
public sealed class RankedList
{
    readonly List<(object Key, int Value)> _entries = [];
    readonly Dictionary<object, int> _ranks = [];

    public RankedList(IEnumerable<(object Key, int Value)> candidates, int dir, Func<int, int> rand)
    {
        foreach (var x in candidates)
        {
            int i = 0;
            while (i < _entries.Count && !Before(x.Value, _entries[i].Value)) i++;
            _entries.Insert(i, x);
        }
        for (int i = 0; i < _entries.Count; i++) _ranks[_entries[i].Key] = i + 1;

        bool Before(int x, int y) => x < y ? dir == 1 : x > y ? dir != 1 : rand(10) < 5;
    }

    public IReadOnlyList<(object Key, int Value)> Entries => _entries;
    public int Count => _entries.Count;
    public int? Rank(object key) => _ranks.TryGetValue(key, out int r) ? r : null;
    internal void Remove(object key) => _entries.RemoveAll(e => e.Key.Equals(key));
}

/// <summary>
/// The planner's list stack: the pick walks the first list, sums each key's
/// ranks over all lists (a key missing from any list is removed from the first list and skipped) and returns the largest sum
/// (strictly larger wins), removing it from the first list.
/// </summary>
public sealed class ListStack
{
    readonly List<RankedList> _lists = [];
    public void Push(RankedList list) => _lists.Add(list);
    public void Clear() => _lists.Clear();
    public int Count => _lists.Count;

    public object? Pick()
    {
        if (_lists.Count == 0) return null;
        var first = _lists[0];
        object? best = null;
        int bestSum = 0;
        foreach (var (key, _) in first.Entries.ToList())
        {
            int sum = 0;
            bool missing = false;
            foreach (var l in _lists)
                if (l.Rank(key) is { } r) sum += r;
                else missing = true;
            if (missing) { first.Remove(key); continue; }
            if (best is null || sum > bestSum) (best, bestSum) = (key, sum);
        }
        if (best is not null) first.Remove(best);
        return best;
    }
}
