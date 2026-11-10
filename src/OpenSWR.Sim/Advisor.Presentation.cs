using OpenSWR.Data.Dat;
using OpenSWR.Data;
using OpenSWR.Sim.Ai;

namespace OpenSWR.Sim;

public sealed partial class Advisor
{
    // ---- voices ----

    /// <summary>
    /// Voice variant list pick: sprite-DLL RCDATA [n, wave...]; n = 0 none, 1 the only wave (no draw), else rand(n). The
    /// out-of-range result n reads the word after the list, never a WAVE in the shipped DLLs: silence, the draw consumed.
    /// </summary>
    public static int Voice(IReadOnlyList<ushort> list, Func<int, int> rand)
    {
        int n = list.Count > 0 ? list[0] : 0;
        if (n <= 0) return 0;
        int r = n == 1 ? 0 : rand(n);
        return r < n && r + 1 < list.Count ? list[r + 1] : 0;
    }

    // ---- strategy music ----


    /// <summary>
    /// Music pick: counter &gt; 0 gives counter -1, t = rand(6) div 2 + 1; else counter := 3 and t by the
    /// mood (Ahead gives 4, SlightlyAhead 5, Strong 6, Weak 7, SlightlyBehind 8, Behind 9, else rand(3) + 1). Returns the MDATA file number (301 ...).
    /// Unguarded: the out-of-range results rand(6) = 6 and rand(3) = 3 both give t = 4, MDATA.304.
    /// </summary>
    public int MusicTrack()
    {
        int t;
        if (MusicCounter > 0)
        {
            MusicCounter--;
            t = Rand(MusicDraw) / 2 + 1;
        }
        else
        {
            MusicCounter = MusicRepeat;
            t = Mood switch
            {
                AdvisorMood.Ahead => 4, AdvisorMood.SlightlyAhead => 5, AdvisorMood.Strong => 6, AdvisorMood.Weak => 7,
                AdvisorMood.SlightlyBehind => 8, AdvisorMood.Behind => 9, _ => Rand(MoodDraw) + 1,
            };
        }
        return Tracks[Math.Min(t, Tracks.Length - 1)];
    }
}
