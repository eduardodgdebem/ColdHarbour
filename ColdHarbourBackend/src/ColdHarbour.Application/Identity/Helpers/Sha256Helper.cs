using System.Security.Cryptography;
using System.Text;

namespace ColdHarbour.Application.Identity.Helpers;

internal static class Sha256Helper
{
    internal static string Hex(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
