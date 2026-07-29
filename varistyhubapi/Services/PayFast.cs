using System.Security.Cryptography;
using System.Text;

namespace VarsityHub.Services;

/// <summary>
/// PayFast helpers: URL-encoding + MD5 signature per PayFast's spec
/// (uppercase-hex encoding with spaces as '+', trimmed values, optional passphrase).
/// </summary>
public static class PayFast
{
    public static string Encode(string? v) => Uri.EscapeDataString(v ?? string.Empty).Replace("%20", "+");

    public static string Signature(IEnumerable<KeyValuePair<string, string>> fields, string? passphrase)
    {
        var sb = new StringBuilder();
        foreach (var (key, value) in fields)
        {
            if (string.IsNullOrEmpty(value)) continue;
            sb.Append(key).Append('=').Append(Encode(value.Trim())).Append('&');
        }
        var data = sb.ToString().TrimEnd('&');
        if (!string.IsNullOrEmpty(passphrase))
            data += "&passphrase=" + Encode(passphrase.Trim());

        return Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(data))).ToLowerInvariant();
    }
}
