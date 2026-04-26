namespace PortfolioSaver.Data.Interfaces;

public interface ISettingsProtectionService
{
    string Protect(string plainText);
    string Unprotect(string protectedText);
}
