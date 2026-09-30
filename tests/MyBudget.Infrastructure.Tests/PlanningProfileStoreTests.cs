using MyBudget.Core;
using MyBudget.Infrastructure;

namespace MyBudget.Infrastructure.Tests;

[TestClass]
public sealed class PlanningProfileStoreTests
{
    private string _directory = null!;
    private PlanningProfileStore _store = null!;

    [TestInitialize]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "MyBudget.Planning.Tests", Guid.NewGuid().ToString("N"));
        _store = new PlanningProfileStore(Path.Combine(_directory, "planning-profile.json"));
    }

    [TestCleanup]
    public void CleanUp()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [TestMethod]
    public async Task MissingProfileDoesNotCreateData()
    {
        Assert.IsNull(await _store.LoadAsync());
        Assert.IsFalse(Directory.Exists(_directory));
    }

    [TestMethod]
    public async Task RoundTripPreservesUnknownAmountsAndOriginalFigures()
    {
        var profile = Example();
        await _store.SaveAsync(profile);
        var loaded = (await _store.LoadAsync())!;
        Assert.AreEqual(profile.Original.ProvisionalTakeHome, loaded.Original.ProvisionalTakeHome);
        Assert.IsNull(loaded.Current.Costs.Single(cost => cost.Id == "unknown").Amount);
        Assert.IsNull(loaded.Payslip!.GrossPay);
        Assert.IsNull(loaded.Payslip.TotalDeductions);
        Assert.IsNull(loaded.ActualSpending!["food"]);
        Assert.IsFalse(Directory.GetFiles(_directory, "*.db").Any());
    }

    [TestMethod]
    public async Task UpdateArchivesPreviousPlanAndKeepsOriginal()
    {
        var profile = Example();
        await _store.SaveAsync(profile);
        await _store.SaveAsync(profile with { Current = profile.Current with { ProvisionalTakeHome = 3100m } });
        var loaded = (await _store.LoadAsync())!;
        Assert.AreEqual(3000m, loaded.Original.ProvisionalTakeHome);
        Assert.AreEqual(3100m, loaded.Current.ProvisionalTakeHome);
        var history = Directory.GetFiles(Path.Combine(_directory, "planning-history"), "*.json");
        Assert.HasCount(1, history);
        Assert.AreEqual(3000m, (await new PlanningProfileStore(history[0]).LoadAsync())!.Current.ProvisionalTakeHome);
    }

    [TestMethod]
    public async Task ChangingOriginalIsRejectedWithoutDamagingProfile()
    {
        var profile = Example();
        await _store.SaveAsync(profile);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => _store.SaveAsync(profile with
        { Original = profile.Original with { ProvisionalTakeHome = 1m } }));
        Assert.AreEqual(3000m, (await _store.LoadAsync())!.Original.ProvisionalTakeHome);
        Assert.HasCount(1, Directory.GetFiles(_directory));
    }

    [TestMethod]
    public async Task InvalidUpdateDoesNotReplaceOrArchiveValidData()
    {
        var profile = Example();
        await _store.SaveAsync(profile);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => _store.SaveAsync(profile with
        { Current = profile.Current with { ProvisionalTakeHome = -1m } }));
        Assert.AreEqual(3000m, (await _store.LoadAsync())!.Current.ProvisionalTakeHome);
        Assert.IsFalse(Directory.Exists(Path.Combine(_directory, "planning-history")));
    }

    [TestMethod]
    public async Task ConcurrentSavesRemainValidAndRetainHistory()
    {
        var profile = Example();
        await _store.SaveAsync(profile);
        await Task.WhenAll(Enumerable.Range(1, 4).Select(index => _store.SaveAsync(profile with
        { Current = profile.Current with { FirstMonthEnvelope = 2000m + index } })));
        Assert.IsNotNull(await _store.LoadAsync());
        Assert.HasCount(4, Directory.GetFiles(Path.Combine(_directory, "planning-history"), "*.json"));
        Assert.IsFalse(Directory.GetFiles(_directory, "*.tmp").Any());
    }

    [TestMethod]
    public async Task UnsupportedSchemaIsRejected()
    {
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => _store.SaveAsync(Example() with { SchemaVersion = 99 }));
        Assert.IsFalse(Directory.Exists(_directory));
    }

    private static PlanningProfile Example()
    {
        var plan = new PlanningDefinition(3600m, 3000m, new DateOnly(2026, 11, 5), 2000m, 1000m,
            50m, new DateOnly(2026, 11, 1), 3000m,
            [new("food", "Food", 400m, PlanningBucket.Needs, "Estimate"),
             new("subscription", "Subscription", 120m, PlanningBucket.Wants, "Current", PlanningPurpose.Subscription),
             new("unknown", "Unconfirmed cost", null, PlanningBucket.Needs, "Unconfirmed")], ["Holdings unconfirmed"]);
        return new PlanningProfile(1, new DateOnly(2026, 9, 30), "MYR", plan, plan,
            new PlanningPayslip(), ActualSpending: new Dictionary<string, decimal?> { ["food"] = null });
    }
}
