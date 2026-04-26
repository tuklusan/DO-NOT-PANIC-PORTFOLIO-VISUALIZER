using System.Security.Cryptography;
using System.Text;
using PortfolioSaver.Data.Interfaces;

namespace PortfolioSaver.Data.Services;

public sealed class SettingsProtectionService : ISettingsProtectionService
{
    public string Protect(string plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
            return string.Empty;

        byte[] data = Encoding.UTF8.GetBytes(plainText);
        byte[] protectedData = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedData);
    }

    public string Unprotect(string protectedText)
    {
        if (string.IsNullOrWhiteSpace(protectedText))
            return string.Empty;

        byte[] data = Convert.FromBase64String(protectedText);
        byte[] plainData = ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plainData);
    }
}
