using HostCraft.Core.Interfaces;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HostCraft.Infrastructure.Security;

/// <summary>
/// EF Core value converter for automatic encryption/decryption of string fields.
/// </summary>
public class EncryptedStringConverter : ValueConverter<string, string>
{
    private static IEncryptionService? _encryptionService;

    public EncryptedStringConverter()
        : base(
            v => Encrypt(v),
            v => Decrypt(v))
    {
    }

    /// <summary>
    /// Initialize the converter with the encryption service.
    /// This should be called during application startup.
    /// </summary>
    public static void Initialize(IEncryptionService encryptionService)
    {
        _encryptionService = encryptionService;
    }

    private static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return plainText;
        }

        if (_encryptionService == null)
        {
            throw new InvalidOperationException("EncryptedStringConverter has not been initialized with an encryption service.");
        }

        return _encryptionService.Encrypt(plainText);
    }

    private static string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
        {
            return cipherText;
        }

        if (_encryptionService == null)
        {
            throw new InvalidOperationException("EncryptedStringConverter has not been initialized with an encryption service.");
        }

        return _encryptionService.Decrypt(cipherText);
    }
}
