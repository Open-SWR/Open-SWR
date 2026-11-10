using System.Security.Cryptography;
using OpenSWR.Data.Media;

namespace OpenSWR.Tests;

public class SmackerTests
{
    // Reference hashes: MD5 of ffmpeg -i <file> -f rawvideo -pix_fmt rgb24 / -f s16le (whole file), as a decimal number.
    [GameTheory]
    [InlineData("MDATA.101", 334, "24953600740759040737027988525140129788", "140292626518965819245620337338713029354")]
    [InlineData("MDATA.000", 259, "44990160339202621049086159467992331661", "268367864399493786329038589436990180571")]
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
        Assert.Equal(videoMd5, Digest.Decimal(video.GetHashAndReset()));
        Assert.Equal(audioMd5, Digest.Decimal(audio.GetHashAndReset()));
    }
}
