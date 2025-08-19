using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CmdKit;

/// <summary>
/// Simple DPAPI based protector for sensitive values. Only works on same user + machine.
/// Format: __ENC__base64
/// </summary>
public static class SecretProtector
{
    public const string Prefix = "__ENC__"; // marker

    public static string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return plain;
        try
        {
            var bytes = Encoding.UTF8.GetBytes(plain);
            var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            return Prefix + Convert.ToBase64String(protectedBytes);
        }
        catch
        {
            return plain; // fallback
        }
    }

    public static string Unprotect(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (!value.StartsWith(Prefix, StringComparison.Ordinal)) return value;
        try
        {
            var b64 = value.Substring(Prefix.Length);
            var protectedBytes = Convert.FromBase64String(b64);
            var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return value; // return original if fails
        }
    }
}
