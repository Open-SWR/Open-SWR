using OpenSWR.Data;
using OpenSWR.Data.Dat;

namespace OpenSWR.Tests;

public class SptFileTests
{
    [GameTheory]
    [InlineData("C3POACT.SPT", "ALSPRITE.DLL", 87)]
    [InlineData("IMP22ACT.SPT", "EMSPRITE.DLL", 85)]
    public void ActionsParseAndReferenceSpriteData(string script, string spriteDll, int count)
    {
        var actions = SptFile.Read(File.ReadAllBytes(GameDir.File(Path.Combine("GData", script))));
        Assert.Equal(count, actions.Count);
        Assert.Equal(Enumerable.Range(1, count).Select(i => (uint)i), actions.Select(a => a.Id));
        Assert.All(actions, a => Assert.Equal((1u, 0u), (a.Enabled, a.Cursor)));

        var steps = actions.SelectMany(a => a.Steps).ToList();
        Assert.All(steps, s => Assert.InRange(s.Flags, 0u, 2u));
        Assert.All(steps, s => Assert.Contains(s.Param1, new uint[] { 0, 2, 3 }));

        // Steps without the briefing-DLL flag reference the side's sprite DLL
        var rcdata = PeResources.Load(GameDir.File(spriteDll)).OfType(ResId.RcData).Select(r => r.Name.Id).ToHashSet();
        Assert.All(steps.Where(s => !s.Briefing), s => Assert.Contains(s.RcId, rcdata));
    }
}
