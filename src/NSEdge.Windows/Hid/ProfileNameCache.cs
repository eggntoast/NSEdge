using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NSEdge.Core;

namespace NSEdge.Windows.Hid;

// Names are stored after successful Sony feature-report reads, and matched by
// the same real controller identity on Bluetooth. No path-based fallback.
internal sealed class ProfileNameCache
{
    private readonly string folder;
    private readonly byte[] secret;

    public ProfileNameCache() : this(Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "NSEdge", "profile-names")) { }

    internal ProfileNameCache(string directory)
    {
        folder = directory;
        Directory.CreateDirectory(folder);
        string keyFile = Path.Combine(folder, "identity.key");
        if (!File.Exists(keyFile))
        {
            byte[] generated = RandomNumberGenerator.GetBytes(32);
            using var file = new FileStream(keyFile, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            file.Write(generated);
            secret = generated;
        }
        else
        {
            secret = File.ReadAllBytes(keyFile);
            if (secret.Length != 32)
                throw new InvalidDataException("Invalid local controller identity key.");
        }
    }

    private string? PathFor(string? identity)
    {
        string? normalized = ControllerIdentity.Normalize(identity);
        if (normalized is null) return null;
        byte[] digest = HMACSHA256.HashData(secret, Encoding.ASCII.GetBytes(normalized));
        return Path.Combine(folder, Convert.ToHexString(digest) + ".json");
    }

    public ProfileNames Read(string? identity)
    {
        string? path = PathFor(identity);
        if (path is null || !File.Exists(path)) return ProfileNames.Empty;
        try
        {
            return JsonSerializer.Deserialize<ProfileNames>(File.ReadAllText(path))
                ?? ProfileNames.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return ProfileNames.Empty;
        }
    }

    public void Save(string? identity, ProfileNames names)
    {
        string? path = PathFor(identity);
        if (path is null || (names.Circle is null && names.Cross is null && names.Square is null))
            return;
        names = Read(identity).MergeRead(names);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(names));
        File.Move(temp, path, overwrite: true);
    }
}
