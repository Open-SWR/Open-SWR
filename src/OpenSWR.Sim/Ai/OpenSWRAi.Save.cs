using System.Collections.Immutable;
using System.Text.Json.Nodes;
using OpenSWR.Data.Dat;

namespace OpenSWR.Sim.Ai;

public sealed partial class OpenSWRController
{

    internal JsonObject Save()
    {
        var refused = new JsonArray();
        foreach (var ((type, target, arg, field), day) in _refused) refused.Add(new JsonArray((int)type, target, arg, day, field));
        var j = new JsonObject { ["decided"] = Decided, ["refused"] = refused };
        if (_lastTaunt is { } last) j["lastTaunt"] = last;
        if (_passed.Count > 0) j["passed"] = new JsonArray([.. _passed.Select(e => (JsonNode)new JsonArray((uint)e.Key, e.Value))]);
        if (_taunts.Any(n => n != 0)) j["taunts"] = new JsonArray([.. _taunts.Select(n => (JsonNode)n)]);
        if (_owned is not null) j["owned"] = new JsonArray([.. _owned.Select(r => (JsonNode)r)]);
        if (_enemyHeld.Count > 0) j["enemyHeld"] = new JsonArray([.. _enemyHeld.Select(r => (JsonNode)r)]);
        if (_launchedAt is { } launched) j["launchedAt"] = launched;
        if (_seen.Count > 0) j["seen"] = new JsonArray([.. _seen.Select(e => (JsonNode)new JsonArray((uint)e.Key, e.Value))]);
        if (_foiled.Count > 0) j["foiled"] = new JsonArray([.. _foiled.Select(e => (JsonNode)new JsonArray((uint)e.Key.Kind, e.Key.Target, e.Value))]);
        if (_foilMarks.Count > 0) j["foilMarks"] = new JsonArray([.. _foilMarks.Select(e => (JsonNode)new JsonArray((uint)e.Key, e.Value.Day, e.Value.Count))]);
        if (_lowSince.Count > 0) j["lowSince"] = new JsonArray([.. _lowSince.Select(e => (JsonNode)new JsonArray((uint)e.Key, e.Value))]);
        SaveAttack(j);
        SaveLandings(j);
        SaveWatch(j);
        SaveExperience(j);
        SaveIntel(j);
        SaveAgents(j);
        SaveIdle(j);
        SaveSwarm(j);
        SaveObjectives(j);
        SaveTroopFit(j);
        SaveBombard(j);
        SaveCascade(j);
        if (RecruitTotal is { } total) j["recruitTotal"] = total;
        if (Recruited > 0) j["recruited"] = Recruited;
        if (EconomyOnly) j["economyOnly"] = true;
        SaveGate(j);
        return j;
    }

    internal void Load(GameState game, int issued, JsonObject? j)
    {
        Issued = issued;
        if (j is null) return;
        Decided = (int)j["decided"]!;
        LoadAttack(game, j);
        LoadLandings(j);
        LoadWatch(j);
        LoadExperience(j);
        LoadIntel(j);
        LoadAgents(j);
        LoadIdle(j);
        LoadSwarm(j);
        LoadObjectives(j);
        LoadTroopFit(j);
        LoadBombard(j);
        LoadCascade(j);
        RecruitTotal = (int?)j["recruitTotal"];
        Recruited = (int?)j["recruited"] ?? 0;
        EconomyOnly = (bool?)j["economyOnly"] ?? false;
        LoadGate(j);
        foreach (var e in j["refused"]!.AsArray()) _refused[((CommandType)(int)e![0]!, (uint)e[1]!, (uint)e[2]!, e.AsArray().Count > 4 ? (uint)e[4]! : 0)] = (int)e[3]!;
        _lastTaunt = (int?)j["lastTaunt"];
        if (j["passed"] is JsonArray passed) foreach (var e in passed) _passed[(uint)e![0]!] = (int)e[1]!;
        if (j["taunts"] is JsonArray taunts) for (int i = 0; i < Math.Min(taunts.Count, _taunts.Length); i++) _taunts[i] = (int)taunts[i]!;
        if (j["owned"] is JsonArray owned) _owned = [.. owned.Select(r => (uint)r!)];
        if (j["enemyHeld"] is JsonArray held) _enemyHeld = [.. held.Select(r => (uint)r!)];
        _launchedAt = (uint?)j["launchedAt"];
        if (j["seen"] is JsonArray seen) foreach (var e in seen) _seen[(uint)e![0]!] = (int)e[1]!;
        if (j["foiled"] is JsonArray foiled) foreach (var e in foiled) _foiled[((MissionKind)(uint)e![0]!, (uint)e[1]!)] = (int)e[2]!;
        if (j["foilMarks"] is JsonArray marks) foreach (var e in marks) _foilMarks[(uint)e![0]!] = ((int)e[1]!, (int)e[2]!);
        if (j["lowSince"] is JsonArray low) foreach (var e in low) _lowSince[(uint)e![0]!] = (int)e[1]!;
    }
}
