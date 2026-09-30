using System.Text.Json;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;

namespace MyBudget.App;

public sealed record NotionConnection(Guid ParentId, string Token);

/// <summary>Credential is protected for this Windows user, outside source and database backups.</summary>
public sealed class NotionConnectionStore(string directory)
{
    private readonly string _path = Path.Combine(directory, "notion-connection.json");

    public async Task SaveAsync(NotionConnection connection)
    {
        var protector = new DataProtectionProvider("LOCAL=user");
        var data = CryptographicBuffer.ConvertStringToBinary(connection.Token, BinaryStringEncoding.Utf8);
        var encrypted = await protector.ProtectAsync(data);
        var stored = new StoredConnection(connection.ParentId, CryptographicBuffer.EncodeToBase64String(encrypted));
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(stored));
        File.Move(temporary, _path, true);
    }

    public async Task<NotionConnection?> LoadAsync()
    {
        if (!File.Exists(_path)) return null;
        if (new FileInfo(_path).Length > 8192) throw new InvalidDataException("The saved connection is invalid.");
        var stored = JsonSerializer.Deserialize<StoredConnection>(await File.ReadAllTextAsync(_path))
            ?? throw new InvalidDataException("The saved connection is invalid.");
        var encrypted = CryptographicBuffer.DecodeFromBase64String(stored.ProtectedToken);
        var data = await new DataProtectionProvider().UnprotectAsync(encrypted);
        return new NotionConnection(stored.ParentId, CryptographicBuffer.ConvertBinaryToString(BinaryStringEncoding.Utf8, data));
    }

    public void Forget() => File.Delete(_path);
    private sealed record StoredConnection(Guid ParentId, string ProtectedToken);
}
