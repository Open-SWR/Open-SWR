using System.Security.Cryptography;
using OpenSWR.Data;
using OpenSWR.Data.Advisor;
using OpenSWR.Data.Dat;

namespace OpenSWR.Tests;

public class AdvisorTests
{
    static readonly ushort[] Registered = [4, 5, 17, 18, 19, 20, 21];

    [GameTheory]
    [InlineData("C3POACT.SPT", "ALSPRITE.DLL")]
    [InlineData("IMP22ACT.SPT", "EMSPRITE.DLL")]
    public void EveryScriptStepResolves(string script, string spriteDll)
    {
        var dll = new SpriteDll(PeResources.Load(GameDir.File(spriteDll)));
        var steps = SptFile.Read(File.ReadAllBytes(GameDir.File(Path.Combine("GData", script))))
            .SelectMany(a => a.Steps).Where(s => !s.Briefing).ToList(); // the briefing DLL is absent in this install

        int clips = 0, silent = 0;
        foreach (var step in steps)
        {
            var command = dll.Command(step.RcId);
            if (!Registered.Contains(command.Type)) continue; // type 3: no prototype, plays nothing
            switch (command.Type)
            {
                case SpriteCommand.Clip or SpriteCommand.VoiceClip:
                    var clip = dll.Clip(command.ResId);
                    var frames = dll.ClipFrames(clip);
                    Assert.Equal(clip.FrameCount, frames.Frames.Count);
                    Assert.Equal(9, clip.VoiceModule);
                    clips++;
                    if (dll.Wave(clip.VoiceWave) is null) silent++;
                    break;
                case SpriteCommand.Still or SpriteCommand.SecondaryStill:
                    dll.Bitmap(dll.Words(command.ResId)[0]);
                    break;
                case SpriteCommand.Relay:
                    var relay = dll.Words(command.ResId);
                    dll.Bitmap(relay[1]);
                    Assert.Contains(dll.Command(relay[2]).Type, Registered);
                    break;
            }
        }
        Assert.True(clips > 50);
        Assert.True(silent < clips / 2, $"{silent} of {clips} clips have no voice WAVE");
    }

    [GameFact]
    public void ClipFramesAddDeltasOntoBase()
    {
        var dll = new SpriteDll(PeResources.Load(GameDir.File("ALSPRITE.DLL")));
        var frames = dll.ClipFrames(new AdvisorClip(0, 0, 9, 7, 2001));
        Assert.Equal((67, 116, 7), (frames.Width, frames.Height, frames.Frames.Count));
        // same reference as RleImageTests.DeltaChainMatchesReference (base 2001 + deltas 2002-2007)
        Assert.Equal("a8645939a932f690386b0e32ffd78dd0", Convert.ToHexStringLower(MD5.HashData(frames.Frames[6])));
        Assert.Equal(SpriteDll.TransparentIndex, frames.Frames[0][0]);
    }
}
