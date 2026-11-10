using System.Buffers.Binary;

namespace OpenSWR.Data.Media;

// Smacker (SMK2) video decoder, written from the public Smacker format description.

public readonly record struct SmackerAudioTrack(int Track, int SampleRate, bool Stereo, bool Is16Bit, bool Compressed);

/// <summary>
/// Decodes one Smacker file frame by frame. After <see cref="NextFrame"/>: <see cref="Pixels"/> holds palette indices
/// (Width x Height, row-major), <see cref="Palette"/> 256 x RGB, <see cref="Audio"/> the PCM bytes of each track for this frame
/// (16-bit little-endian signed or 8-bit unsigned, interleaved when stereo).
/// </summary>
public sealed class SmackerVideo
{
    public int Width { get; }
    public int Height { get; }
    public int FrameCount { get; }
    /// <summary>Milliseconds per frame.</summary>
    public double FrameDuration { get; }
    public uint Flags { get; }
    public IReadOnlyList<SmackerAudioTrack> AudioTracks { get; }

    public byte[] Pixels { get; }
    public byte[] Palette { get; } = new byte[768];
    public byte[]?[] Audio { get; } = new byte[7][];
    public int FrameIndex { get; private set; } = -1;

    readonly byte[] _file;
    readonly uint[] _frameSizes;
    readonly byte[] _frameTypes;
    readonly BigTree _mmap, _mclr, _full, _type;
    int _next;

    const int MaxTreeDepth = 256;

    static readonly int[] BlockRuns = [.. Enumerable.Range(1, 59), 128, 256, 512, 1024, 2048];

    public SmackerVideo(byte[] file)
    {
        if (file.Length < 104 || !file.AsSpan(0, 4).SequenceEqual("SMK2"u8)) throw new InvalidDataException("Not a Smacker 2 file.");
        _file = file;
        Width = (int)U32(4);
        Height = (int)U32(8);
        FrameCount = (int)U32(12);
        int rate = (int)U32(16);
        FrameDuration = rate > 0 ? rate : rate < 0 ? -rate / 100.0 : 100;
        Flags = U32(20);
        if ((Flags & 0b110) != 0) throw new NotSupportedException("Interlaced / doubled Smacker files are not supported.");

        var tracks = new List<SmackerAudioTrack>();
        for (int t = 0; t < 7; t++)
        {
            uint a = U32(72 + t * 4);
            if ((a & 0x40000000) != 0)
                tracks.Add(new SmackerAudioTrack(t, (int)(a & 0xFFFFFF), (a & 0x10000000) != 0, (a & 0x20000000) != 0, (a & 0x80000000) != 0));
        }
        AudioTracks = tracks;

        int treesSize = (int)U32(52), at = 104;
        // before any allocation sized by the header
        if (Width is < 1 or > 4096 || Height is < 1 or > 4096 || FrameCount is < 0 or > 1 << 20 || treesSize < 0 || (long)at + 4L * FrameCount + FrameCount + treesSize > file.Length)
            throw new InvalidDataException("Smacker header out of range.");
        Pixels = new byte[Width * Height];
        _frameSizes = new uint[FrameCount];
        for (int i = 0; i < FrameCount; i++, at += 4) _frameSizes[i] = U32(at);
        _frameTypes = file.AsSpan(at, FrameCount).ToArray();
        at += FrameCount;

        var bits = new BitReader(file, at, treesSize);
        _mmap = new BigTree(bits);
        _mclr = new BigTree(bits);
        _full = new BigTree(bits);
        _type = new BigTree(bits);
        _next = at + treesSize;
    }

    uint U32(int at) => BinaryPrimitives.ReadUInt32LittleEndian(_file.AsSpan(at));

    /// <summary>Decodes the next frame; false after the last frame.</summary>
    public bool NextFrame()
    {
        if (FrameIndex + 1 >= FrameCount) return false;
        FrameIndex++;
        int at = _next, end = at + (int)(_frameSizes[FrameIndex] & ~3u);
        _next = end;
        byte type = _frameTypes[FrameIndex];

        if ((type & 1) != 0)
        {
            int length = _file[at] * 4;
            UpdatePalette(_file.AsSpan(at + 1, length - 1));
            at += length;
        }

        Array.Clear(Audio);
        for (int t = 0; t < 7; t++)
        {
            if ((type & (2 << t)) == 0) continue;
            int length = (int)U32(at);
            var track = AudioTracks.FirstOrDefault(a => a.Track == t);
            Audio[t] = track.Compressed ? DecodeAudio(_file, at + 4, length - 4, track) : _file.AsSpan(at + 4, length - 4).ToArray();
            at += length;
        }

        DecodeVideo(new BitReader(_file, at, end - at));
        return true;
    }

    // Palette chunk: 0x80 | n -> keep n+1 entries; 0x40 | n, src -> copy n+1 entries from the old palette at src; else 6-bit r, g, b.
    void UpdatePalette(ReadOnlySpan<byte> data)
    {
        var old = Palette.ToArray();
        int p = 0;
        for (int i = 0; i < data.Length && p < 256;)
        {
            byte b = data[i++];
            if ((b & 0x80) != 0)
                p += (b & 0x7F) + 1;
            else if ((b & 0x40) != 0)
            {
                int count = (b & 0x3F) + 1, src = data[i++];
                old.AsSpan(src * 3, count * 3).CopyTo(Palette.AsSpan(p * 3));
                p += count;
            }
            else
            {
                Palette[p * 3] = Expand6(b);
                Palette[p * 3 + 1] = Expand6(data[i++]);
                Palette[p * 3 + 2] = Expand6(data[i++]);
                p++;
            }
        }
    }

    static byte Expand6(byte v) => (byte)(((v & 0x3F) << 2) | ((v & 0x3F) >> 4));

    void DecodeVideo(BitReader bits)
    {
        _mmap.ResetRecent();
        _mclr.ResetRecent();
        _full.ResetRecent();
        _type.ResetRecent();

        int blocksPerRow = Width / 4, total = blocksPerRow * (Height / 4);
        for (int block = 0; block < total;)
        {
            int type = _type.Decode(bits), run = BlockRuns[(type >> 2) & 0x3F];
            switch (type & 3)
            {
                case 0: // two-colour block: colours from MClr, 16-bit pixel mask from MMap (bit set = high colour)
                    for (; run > 0 && block < total; run--, block++)
                    {
                        int colours = _mclr.Decode(bits), mask = _mmap.Decode(bits);
                        int at = BlockStart(block, blocksPerRow);
                        for (int y = 0; y < 4; y++, at += Width)
                            for (int x = 0; x < 4; x++, mask >>= 1)
                                Pixels[at + x] = (byte)((mask & 1) != 0 ? colours >> 8 : colours);
                    }
                    break;
                case 1: // full block: per row two values from Full, the first one is the right pixel pair
                    for (; run > 0 && block < total; run--, block++)
                    {
                        int at = BlockStart(block, blocksPerRow);
                        for (int y = 0; y < 4; y++, at += Width)
                        {
                            int right = _full.Decode(bits), left = _full.Decode(bits);
                            Pixels[at + 2] = (byte)right;
                            Pixels[at + 3] = (byte)(right >> 8);
                            Pixels[at] = (byte)left;
                            Pixels[at + 1] = (byte)(left >> 8);
                        }
                    }
                    break;
                case 2: // unchanged
                    block += run;
                    break;
                case 3: // solid colour
                    for (; run > 0 && block < total; run--, block++)
                    {
                        int at = BlockStart(block, blocksPerRow);
                        for (int y = 0; y < 4; y++, at += Width) Pixels.AsSpan(at, 4).Fill((byte)(type >> 8));
                    }
                    break;
            }
        }
    }

    int BlockStart(int block, int blocksPerRow) => (block / blocksPerRow) * 4 * Width + (block % blocksPerRow) * 4;

    // Compressed audio: u32 unpacked size, then bitstream: flags, Huffman trees, first sample(s), then per-sample deltas.
    static byte[] DecodeAudio(byte[] file, int at, int length, SmackerAudioTrack track)
    {
        uint unpacked = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at));
        if (unpacked > 1 << 28 || length < 4) throw new InvalidDataException("Smacker audio chunk size out of range.");
        int size = (int)unpacked;
        var output = new byte[size];
        var bits = new BitReader(file, at + 4, length - 4);
        if (bits.Bit() == 0) return output;
        bool stereo = bits.Bit() == 1, is16 = bits.Bit() == 1;
        if (stereo != track.Stereo || is16 != track.Is16Bit) throw new InvalidDataException("Audio chunk format differs from header.");

        var trees = new ByteTree[1 << ((stereo ? 1 : 0) + (is16 ? 1 : 0))];
        for (int i = 0; i < trees.Length; i++) trees[i] = ByteTree.ReadOptional(bits);

        int channels = stereo ? 2 : 1;
        var predictor = new int[2];
        if (is16)
        {
            for (int c = channels - 1; c >= 0; c--) predictor[c] = (short)((bits.Bits(8) << 8) | bits.Bits(8));
            int samples = size / 2;
            for (int i = 0; i < samples; i++)
            {
                int c = i % channels;
                if (i >= channels)
                {
                    int lo = trees[c * 2].Decode(bits), hi = trees[c * 2 + 1].Decode(bits);
                    predictor[c] = (short)(predictor[c] + (short)(lo | hi << 8));
                }
                BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(i * 2), (short)predictor[c]);
            }
        }
        else
        {
            for (int c = channels - 1; c >= 0; c--) predictor[c] = bits.Bits(8);
            for (int i = 0; i < size; i++)
            {
                int c = i % channels;
                if (i >= channels) predictor[c] = (byte)(predictor[c] + (sbyte)trees[c].Decode(bits));
                output[i] = (byte)predictor[c];
            }
        }
        return output;
    }

    /// <summary>Little-endian bit reader: bits are taken from the least significant end of each byte.</summary>
    sealed class BitReader(byte[] data, int start, int length)
    {
        long _bit = (long)start * 8;
        readonly long _end = Math.Min(((long)start + length) * 8, (long)data.Length * 8);

        public int Bit()
        {
            if (_bit >= _end) throw new InvalidDataException("Smacker bitstream overrun.");
            int b = (data[_bit >> 3] >> (int)(_bit & 7)) & 1;
            _bit++;
            return b;
        }

        public int Bits(int n)
        {
            int v = 0;
            for (int i = 0; i < n; i++) v |= Bit() << i;
            return v;
        }
    }

    /// <summary>Huffman tree with 8-bit leaves. Stored as: bit 1 = node (0-branch subtree, then 1-branch), bit 0 = leaf + 8-bit value.</summary>
    sealed class ByteTree
    {
        readonly List<(int Zero, int One)> _nodes = [];
        int _root;

        ByteTree() { }

        /// <summary>Presence bit, tree (absent = single leaf 0), terminating 0 bit.</summary>
        public static ByteTree ReadOptional(BitReader bits)
        {
            var tree = new ByteTree();
            tree._root = bits.Bit() == 1 ? tree.Read(bits) : ~0;
            if (bits.Bit() != 0) throw new InvalidDataException("Missing Smacker tree terminator.");
            return tree;
        }

        int Read(BitReader bits, int depth = 0)
        {
            if (bits.Bit() == 0) return ~bits.Bits(8);
            if (depth > MaxTreeDepth) throw new InvalidDataException("Smacker tree too deep."); // each 1 bit recurses: a long run of them would overflow the stack
            int index = _nodes.Count;
            _nodes.Add((0, 0));
            int zero = Read(bits, depth + 1);
            _nodes[index] = (zero, Read(bits, depth + 1));
            return index;
        }

        public int Decode(BitReader bits)
        {
            int n = _root;
            while (n >= 0) n = bits.Bit() == 0 ? _nodes[n].Zero : _nodes[n].One;
            return ~n;
        }
    }

    /// <summary>
    /// Huffman tree with 16-bit leaves built from a low-byte and a high-byte tree. Three escape values mark leaves that
    /// instead return one of the last three distinct decoded values (reset each frame).
    /// </summary>
    sealed class BigTree
    {
        readonly List<(int Zero, int One)> _nodes = [];
        readonly List<int> _leaves = [];
        readonly int[] _recent = new int[3]; // leaf indices holding the last three values
        readonly int _root;

        public BigTree(BitReader bits)
        {
            if (bits.Bit() == 0) // absent: every decode yields 0 without reading bits
            {
                _leaves.Add(0);
                _root = ~0;
                for (int i = 0; i < 3; i++) _recent[i] = AddLeaf(0);
                return;
            }
            var low = ByteTree.ReadOptional(bits);
            var high = ByteTree.ReadOptional(bits);
            int[] escapes = [bits.Bits(16), bits.Bits(16), bits.Bits(16)];
            Array.Fill(_recent, -1);
            _root = Read(bits, low, high, escapes);
            if (bits.Bit() != 0) throw new InvalidDataException("Missing Smacker tree terminator.");
            for (int i = 0; i < 3; i++)
                if (_recent[i] < 0) _recent[i] = AddLeaf(0);
        }

        int AddLeaf(int value)
        {
            _leaves.Add(value);
            return _leaves.Count - 1;
        }

        int Read(BitReader bits, ByteTree low, ByteTree high, int[] escapes, int depth = 0)
        {
            if (bits.Bit() == 0)
            {
                int value = low.Decode(bits) | high.Decode(bits) << 8, leaf = AddLeaf(value);
                for (int i = 0; i < 3; i++)
                    if (value == escapes[i] && _recent[i] < 0)
                    {
                        _recent[i] = leaf;
                        _leaves[leaf] = 0;
                        break;
                    }
                return ~leaf;
            }
            if (depth > MaxTreeDepth) throw new InvalidDataException("Smacker tree too deep.");
            int index = _nodes.Count;
            _nodes.Add((0, 0));
            int zero = Read(bits, low, high, escapes, depth + 1);
            _nodes[index] = (zero, Read(bits, low, high, escapes, depth + 1));
            return index;
        }

        public void ResetRecent()
        {
            foreach (int leaf in _recent) _leaves[leaf] = 0;
        }

        public int Decode(BitReader bits)
        {
            int n = _root;
            while (n >= 0) n = bits.Bit() == 0 ? _nodes[n].Zero : _nodes[n].One;
            int value = _leaves[~n];
            if (value != _leaves[_recent[0]])
            {
                _leaves[_recent[2]] = _leaves[_recent[1]];
                _leaves[_recent[1]] = _leaves[_recent[0]];
                _leaves[_recent[0]] = value;
            }
            return value;
        }
    }
}
