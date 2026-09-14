using System.Security.Cryptography;

namespace SmartX.Api.Services;

// =====================================================================
// ASSIGNMENT REQUIREMENT: MEDIA/LOG ATTACHMENT ENCRYPTION
// Encrypts every uploaded sensor media/log file at rest using AES-256.
// The IV is written as a small unencrypted header at the start of each
// stored file so it can be decrypted again without a separate side table.
// =====================================================================
public class FileEncryptionService
{
    private readonly byte[] _key;

    public FileEncryptionService(IConfiguration config)
    {
        // In production this key would come from a secrets manager (e.g. Azure
        // Key Vault) rather than appsettings - see the "Encryption" section in
        // appsettings.json for the development fallback used here.
        var keyBase64 = config["Encryption:Key"]
            ?? throw new InvalidOperationException("Encryption:Key is not configured.");
        _key = Convert.FromBase64String(keyBase64);
    }

    public async Task EncryptToFileAsync(Stream input, string destinationPath)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();

        await using var outputStream = File.Create(destinationPath);
        await outputStream.WriteAsync(aes.IV);

        await using var cryptoStream = new CryptoStream(outputStream, aes.CreateEncryptor(), CryptoStreamMode.Write);
        await input.CopyToAsync(cryptoStream);
    }

    public async Task<byte[]> DecryptFileAsync(string sourcePath)
    {
        await using var inputStream = File.OpenRead(sourcePath);
        using var aes = Aes.Create();
        aes.Key = _key;

        var iv = new byte[aes.BlockSize / 8];
        var read = 0;
        while (read < iv.Length)
        {
            var chunk = await inputStream.ReadAsync(iv.AsMemory(read, iv.Length - read));
            if (chunk == 0) throw new InvalidDataException("Encrypted file is truncated.");
            read += chunk;
        }
        aes.IV = iv;

        await using var cryptoStream = new CryptoStream(inputStream, aes.CreateDecryptor(), CryptoStreamMode.Read);
        using var output = new MemoryStream();
        await cryptoStream.CopyToAsync(output);
        return output.ToArray();
    }
}
