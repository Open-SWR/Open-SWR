using System.Numerics;
using System.Security.Cryptography;

namespace OpenSWR.Tests;

/// <summary>Hash digests written as one decimal number (the digest bytes read as a big-endian unsigned integer).</summary>
static class Digest
{
    public static string Decimal(byte[] hash) => new BigInteger(hash, isUnsigned: true, isBigEndian: true).ToString();

    public static string Md5(byte[] data) => Decimal(MD5.HashData(data));
}
