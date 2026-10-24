using System.Security.Cryptography;

namespace MenuToOrder.Extraction;

public sealed record MenuImage(byte[] Bytes, string MediaType)
{
    public string Hash { get; } = Convert.ToHexStringLower(SHA256.HashData(Bytes));
}
