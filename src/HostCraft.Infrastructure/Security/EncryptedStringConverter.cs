using HostCraft.Core.Interfaces;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HostCraft.Infrastructure.Security;

/// <summary>
/// EF Core value converter for automatic encryption/decryption of string fields.
/// </summary>
public class EncryptedStringConverter : ValueConverter<string, string>
{
    public EncryptedStringConverter(IEncryptionService encryptionService)
        : base(
            v => encryptionService.Encrypt(v),
            v => encryptionService.Decrypt(v))
    {
    }
}
