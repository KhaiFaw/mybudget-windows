using System.Text.Json;
using System.Text.Json.Serialization;
using MyBudget.Core;

namespace MyBudget.Infrastructure;

/// <summary>Private local estimates, stored separately from the transaction database and build output.</summary>
public sealed class PlanningProfileStore(string path)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };
    private readonly SemaphoreSlim _gate = new(1, 1);
    public string ProfilePath { get; } = Path.GetFullPath(path);

    public async Task<PlanningProfile?> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await ReadAsync(cancellationToken); }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(PlanningProfile profile, CancellationToken cancellationToken = default)
    {
        PlanningCalculator.Validate(profile);
        await _gate.WaitAsync(cancellationToken);
        string? temporary = null;
        try
        {
            var existing = await ReadAsync(cancellationToken);
            if (existing is not null &&
                (JsonSerializer.Serialize(existing.Original, Options) != JsonSerializer.Serialize(profile.Original, Options) ||
                 existing.RecordedOn != profile.RecordedOn || existing.CurrencyCode != profile.CurrencyCode))
                throw new ArgumentException("Original planning figures are preserved. Change the current plan, not the original comparison.");
            var directory = Path.GetDirectoryName(ProfilePath)!;
            Directory.CreateDirectory(directory);
            temporary = Path.Combine(directory, $".planning-{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(profile, Options), cancellationToken);
            if (existing is not null)
            {
                var historyDirectory = Path.Combine(directory, "planning-history");
                Directory.CreateDirectory(historyDirectory);
                File.Copy(ProfilePath, Path.Combine(historyDirectory, $"plan-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json"));
            }
            File.Move(temporary, ProfilePath, overwrite: true);
            temporary = null;
        }
        finally
        {
            if (temporary is not null && File.Exists(temporary)) File.Delete(temporary);
            _gate.Release();
        }
    }

    private async Task<PlanningProfile?> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(ProfilePath)) return null;
        var json = await File.ReadAllTextAsync(ProfilePath, cancellationToken);
        var profile = JsonSerializer.Deserialize<PlanningProfile>(json, Options)
            ?? throw new InvalidDataException("The planning profile is empty.");
        PlanningCalculator.Validate(profile);
        return profile;
    }
}
