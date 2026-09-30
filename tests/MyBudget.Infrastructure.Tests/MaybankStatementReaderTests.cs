using MyBudget.Core;

namespace MyBudget.Infrastructure.Tests;

[TestClass]
public sealed class MaybankStatementReaderTests
{
    private static StatementPage Sample() => new([
        new("Maybank Islamic Berhad", .3, .06), new("SAVINGS ACCOUNT-I", .80, .20),
        new("30/09/26", .85, .12), new("111111-222222", .82, .16),
        new("ENTRY DATE", .07, .25), new("TRANSACTION DESCRIPTION", .32, .25),
        new("TRANSACTION AMOUNT", .64, .25), new("STATEMENT BALANCE", .82, .25),
        new("BEGINNING BALANCE", .18, .28), new("100.00", .88, .28),
        new("01/09/26", .07, .30), new("SALE DEBIT", .18, .30), new("10.00-", .70, .30), new("90.00", .88, .30),
        new("SAMPLE SHOP *", .19, .32),
        new("02/09/26", .07, .34), new("FUND TRANSFER", .18, .34), new("25.00+", .70, .34), new("115.00", .88, .34),
        new("ENDING BALANCE :", .18, .40), new("115.00", .70, .40),
        new("TOTAL CREDIT :", .18, .42), new("25.00", .70, .42),
        new("TOTAL DEBIT :", .18, .44), new("10.00", .70, .44),
        new("Perhatian / Note", .05, .9)]);

    [TestMethod]
    public void SignedRows_ReconcileAndUseStableIds()
    {
        var result = MaybankStatementReader.Parse([Sample()]);
        Assert.AreEqual(new BudgetMonth(2026, 9), result.Month);
        Assert.HasCount(2, result.Entries);
        Assert.AreEqual(-10m, result.Entries[0].SignedAmount);
        Assert.AreEqual(25m, result.Entries[1].SignedAmount);
        Assert.AreEqual("SALE DEBIT · SAMPLE SHOP", result.Entries[0].Description);
        Assert.AreEqual(result.Entries[0].Id, MaybankStatementReader.Parse([Sample()]).Entries[0].Id);
        Assert.AreEqual(115m, result.ClosingBalance);
    }

    [TestMethod]
    public void AccountFingerprintSeparatesOtherwiseIdenticalAccounts()
    {
        var other = Replace("111111-222222", "111111-333333");
        Assert.AreNotEqual(MaybankStatementReader.Parse([Sample()]).Entries[0].Id,
            MaybankStatementReader.Parse([other]).Entries[0].Id);
    }

    [TestMethod]
    [DataRow("10.00-", "10.00")]
    [DataRow("90.00", "91.00")]
    [DataRow("25.00", "26.00")]
    [DataRow("SAVINGS ACCOUNT-I", "CREDIT CARD")]
    [DataRow("02/09/26", "02/08/26")]
    [DataRow("01/09/26", "01/13/26")]
    [DataRow("111111-222222", "unknown")]
    [DataRow("BEGINNING BALANCE", "BALANCE")]
    [DataRow("TOTAL DEBIT :", "UNKNOWN TOTAL")]
    public void AmbiguousOrUnbalancedStatementsFailClosed(string before, string after) =>
        Assert.ThrowsExactly<InvalidDataException>(() => MaybankStatementReader.Parse([Replace(before, after)]));

    [TestMethod]
    public void ContinuationAcrossPageBoundaryStaysWithOriginalRow()
    {
        var words = Sample().Words;
        var header = words.Where(word => word.Top <= .25).ToArray();
        var footer = words.Where(word => word.Top > .8).ToArray();
        var first = new StatementPage([.. header, .. words.Where(word => word.Top > .25 && word.Top < .32), .. footer]);
        var second = new StatementPage([.. header, .. words.Where(word => word.Top >= .32 && word.Top < .8), .. footer]);
        var result = MaybankStatementReader.Parse([first, second]);
        Assert.AreEqual("SALE DEBIT · SAMPLE SHOP", result.Entries[0].Description);
        Assert.AreEqual(MaybankStatementReader.Parse([Sample()]).Entries[0].Id, result.Entries[0].Id);
    }

    [TestMethod]
    public void RepeatedOrIncompletePagesAreRejected()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => MaybankStatementReader.Parse([Sample(), Sample()]));
        Assert.ThrowsExactly<InvalidDataException>(() => MaybankStatementReader.Parse([
            new StatementPage(Sample().Words.Where(word => word.Top < .39 || word.Top > .8).ToArray())]));
        Assert.ThrowsExactly<InvalidDataException>(() => MaybankStatementReader.Parse([new StatementPage([])]));
    }

    [TestMethod]
    public async Task OptionalPrivatePdf_ValidatesLocallyWithoutPersistingOrPrintingContents()
    {
        var path = Environment.GetEnvironmentVariable("MYBUDGET_TEST_STATEMENT_PATH");
        if (string.IsNullOrWhiteSpace(path)) Assert.Inconclusive("Optional private fixture is not configured.");
        var statement = await MaybankStatementReader.ReadAsync(path!);
        Assert.IsGreaterThan(0, statement.Entries.Count);
        Assert.AreEqual(statement.OpeningBalance + statement.TotalCredits - statement.TotalDebits, statement.ClosingBalance);
        Assert.AreEqual(statement.Entries.Count, statement.Entries.Select(row => row.Id).Distinct().Count());
        Assert.IsTrue(statement.Entries.All(row => statement.Month.Contains(row.Date)));
    }

    private static StatementPage Replace(string before, string after) => new(Sample().Words
        .Select(word => word.Text == before ? word with { Text = after } : word).ToArray());
}
