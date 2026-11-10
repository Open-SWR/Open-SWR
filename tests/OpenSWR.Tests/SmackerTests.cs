using System.Security.Cryptography;
using OpenSWR.Data.Media;

namespace OpenSWR.Tests;

public class SmackerTests
{
    // Reference hashes: ffmpeg -i <file> -f rawvideo -pix_fmt rgb24 / -f s16le (whole file).
    [GameTheory]
    [InlineData("MDATA.101", 334, "12c5e3811a20bf9afd9944e0f9a7b7fc", "698b6085a01747f0ab243d721c7abaea")]
    [InlineData("MDATA.000", 259, "21d8ca29c7908e310b8938f0b9fc358d", "c9e5c5471421238379c2f285bfa5ccdb")]
    public void DecodeMatchesFfmpeg(string file, int frames, string videoMd5, string audioMd5)
    {
        var smk = new SmackerVideo(File.ReadAllBytes(GameDir.File(Path.Combine("MDATA", file))));
        Assert.Equal(frames, smk.FrameCount);
        Assert.Equal(66.66, smk.FrameDuration);
        var track = Assert.Single(smk.AudioTracks);
        Assert.Equal(new SmackerAudioTrack(0, 11025, true, true, true), track);

        using var video = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        using var audio = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        var rgb = new byte[smk.Pixels.Length * 3];
        while (smk.NextFrame())
        {
            for (int i = 0; i < smk.Pixels.Length; i++)
                smk.Palette.AsSpan(smk.Pixels[i] * 3, 3).CopyTo(rgb.AsSpan(i * 3));
            video.AppendData(rgb);
            if (smk.Audio[0] is { } pcm) audio.AppendData(pcm);
        }
        Assert.Equal(frames - 1, smk.FrameIndex);
        Assert.Equal(videoMd5, Convert.ToHexStringLower(video.GetHashAndReset()));
        Assert.Equal(audioMd5, Convert.ToHexStringLower(audio.GetHashAndReset()));
    }
}
