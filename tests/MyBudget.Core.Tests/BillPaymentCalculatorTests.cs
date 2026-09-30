namespace MyBudget.Core.Tests;

[TestClass]
public sealed class BillPaymentCalculatorTests
{
    private static readonly BudgetMonth September = new(2026, 9);

    private static RecurringBill Bill(long id = 1, int dueDay = 14) => new(id, $"Bill {id}", 45m, dueDay, 4)
    {
        PaymentTrackingStart = September.FirstDay
    };

    private static BillPayment Paid(RecurringBill bill, BudgetMonth month, DateOnly? paidOn = null) =>
        new(bill.Id, month, RecurringDateCalculator.GetDueDate(month, bill.DueDay),
            paidOn ?? month.FirstDay, bill.Amount);

    [TestMethod]
    [DataRow(5)]
    [DataRow(14)]
    [DataRow(20)]
    public void Paid_EarlyOnTimeOrLate_SkipsToNextMonth(int paidDay)
    {
        var bill = Bill();
        var today = new DateOnly(2026, 9, paidDay);
        var payment = Paid(bill, September, today);
        var next = Assert.ContainsSingle(BillPaymentCalculator.GetNextUnpaidBills([bill], [payment], today));
        Assert.AreEqual(new DateOnly(2026, 10, 14), next.DueDate);
        Assert.AreEqual(next.DueDate.DayNumber - today.DayNumber, next.DaysUntilDue);
        Assert.IsTrue(Assert.ContainsSingle(BillPaymentCalculator.GetMonth([bill], [payment], September)).IsPaid);
        Assert.IsFalse(Assert.ContainsSingle(BillPaymentCalculator.GetMonth([bill], [payment], September.Next)).IsPaid);
    }

    [TestMethod]
    public void EarlyPayment_BelongsToDueMonthNotMonthItWasPaid()
    {
        var bill = Bill();
        var payments = new[] { Paid(bill, September), Paid(bill, September.Next, new DateOnly(2026, 9, 20)) };
        var next = Assert.ContainsSingle(BillPaymentCalculator.GetNextUnpaidBills([bill], payments, September.LastDay));
        Assert.AreEqual(new DateOnly(2026, 11, 14), next.DueDate);
        Assert.IsTrue(Assert.ContainsSingle(BillPaymentCalculator.GetMonth([bill], payments, September.Next)).IsPaid);
    }

    [TestMethod]
    public void Unpaid_StaysOverdueAfterDueDayAndAcrossMonthRollover()
    {
        var bill = Bill();
        foreach (var today in new[] { September.LastDay, new DateOnly(2026, 10, 2) })
        {
            var next = Assert.ContainsSingle(BillPaymentCalculator.GetNextUnpaidBills([bill], [], today));
            Assert.AreEqual(new DateOnly(2026, 9, 14), next.DueDate);
            Assert.IsLessThan(0, next.DaysUntilDue);
        }
    }

    [TestMethod]
    public void PartiallyPaidMonth_OtherUnpaidBillRemainsFirst()
    {
        var first = Bill(1, 10);
        var second = Bill(2, 20);
        var next = BillPaymentCalculator.GetNextUnpaidBills([first, second], [Paid(first, September)], new DateOnly(2026, 9, 5));
        Assert.AreEqual(second.Id, next[0].Bill.Id);
        Assert.AreEqual(new DateOnly(2026, 9, 20), next[0].DueDate);
        Assert.AreEqual(new DateOnly(2026, 10, 10), next[1].DueDate);
        var monthly = BillPaymentCalculator.GetMonth([first, second], [Paid(first, September)], September);
        Assert.IsFalse(monthly[0].IsPaid);
        Assert.IsTrue(monthly[1].IsPaid);
    }

    [TestMethod]
    public void Undo_OriginalOccurrenceBecomesNextAgain()
    {
        var bill = Bill();
        var next = Assert.ContainsSingle(BillPaymentCalculator.GetNextUnpaidBills([bill], [], September.FirstDay));
        Assert.AreEqual(new DateOnly(2026, 9, 14), next.DueDate);
        Assert.IsFalse(Assert.ContainsSingle(BillPaymentCalculator.GetMonth([bill], [], September)).IsPaid);
    }

    [TestMethod]
    public void MonthEnd_ClampsLeapFebruaryThenRestoresMarch31()
    {
        var february = new BudgetMonth(2028, 2);
        var bill = Bill(dueDay: 31) with { PaymentTrackingStart = february.FirstDay };
        var monthly = Assert.ContainsSingle(BillPaymentCalculator.GetMonth([bill], [], february));
        Assert.AreEqual(new DateOnly(2028, 2, 29), monthly.DueDate);
        var next = Assert.ContainsSingle(BillPaymentCalculator.GetNextUnpaidBills([bill], [Paid(bill, february)], february.FirstDay));
        Assert.AreEqual(new DateOnly(2028, 3, 31), next.DueDate);
    }

    [TestMethod]
    public void DecemberPayment_AdvancesYearAndDoesNotPayNextDecember()
    {
        var december = new BudgetMonth(2026, 12);
        var bill = Bill() with { PaymentTrackingStart = december.FirstDay };
        var payment = Paid(bill, december);
        var next = Assert.ContainsSingle(BillPaymentCalculator.GetNextUnpaidBills([bill], [payment], december.FirstDay));
        Assert.AreEqual(new DateOnly(2027, 1, 14), next.DueDate);
        Assert.IsFalse(Assert.ContainsSingle(BillPaymentCalculator.GetMonth([bill], [payment], new BudgetMonth(2027, 12))).IsPaid);
    }

    [TestMethod]
    public void ScheduleEdit_PreservesPaidDateAndAmountButUpdatesNextMonth()
    {
        var bill = Bill();
        var payment = Paid(bill, September);
        var edited = bill with { Amount = 99m, DueDay = 25 };
        var monthly = Assert.ContainsSingle(BillPaymentCalculator.GetMonth([edited], [payment], September));
        Assert.AreEqual(45m, monthly.Amount);
        Assert.AreEqual(new DateOnly(2026, 9, 14), monthly.DueDate);
        var next = Assert.ContainsSingle(BillPaymentCalculator.GetNextUnpaidBills([edited], [payment], September.FirstDay));
        Assert.AreEqual(new DateOnly(2026, 10, 25), next.DueDate);
        Assert.AreEqual(99m, next.Bill.Amount);
    }

    [TestMethod]
    public void Lifetime_RespectsFutureStartInactiveAndEndDatesAndKeepsPaidHistory()
    {
        var bill = Bill();
        var inactive = bill with { IsActive = false };
        Assert.IsEmpty(BillPaymentCalculator.GetNextUnpaidBills([inactive], [], September.FirstDay));
        Assert.IsTrue(Assert.ContainsSingle(BillPaymentCalculator.GetMonth([inactive], [Paid(bill, September)], September)).IsPaid);
        var future = bill with { StartDate = new DateOnly(2026, 11, 20) };
        Assert.AreEqual(new DateOnly(2026, 12, 14), Assert.ContainsSingle(
            BillPaymentCalculator.GetNextUnpaidBills([future], [], September.FirstDay)).DueDate);
        var ended = bill with { EndDate = September.LastDay };
        Assert.IsEmpty(BillPaymentCalculator.GetNextUnpaidBills([ended], [Paid(bill, September)], September.FirstDay));
    }

    [TestMethod]
    public void MaximumCalendarMonth_DoesNotOverflowAfterLastPayment()
    {
        var month = new BudgetMonth(9999, 12);
        var bill = Bill(dueDay: 31) with { PaymentTrackingStart = month.FirstDay };
        Assert.IsEmpty(BillPaymentCalculator.GetNextUnpaidBills([bill], [Paid(bill, month)], month.FirstDay));
    }

    [TestMethod]
    public void InvalidMonth_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BillPaymentCalculator.GetMonth([Bill()], [], default));
    }
}
