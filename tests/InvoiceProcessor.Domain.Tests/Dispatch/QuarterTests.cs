using InvoiceProcessor.Domain.Dispatch;

namespace InvoiceProcessor.Domain.Tests.Dispatch;

public sealed class QuarterTests
{
    // Q1-Q4 months
    [Fact]
    public void Q1_Months_AreJanuaryToMarch()
    {
        var q = new Quarter(2026, 1);
        Assert.Equal([1, 2, 3], q.Months);
    }

    [Fact]
    public void Q2_Months_AreAprilToJune()
    {
        var q = new Quarter(2026, 2);
        Assert.Equal([4, 5, 6], q.Months);
    }

    [Fact]
    public void Q3_Months_AreJulyToSeptember()
    {
        var q = new Quarter(2026, 3);
        Assert.Equal([7, 8, 9], q.Months);
    }

    [Fact]
    public void Q4_Months_AreOctoberToDecember()
    {
        var q = new Quarter(2026, 4);
        Assert.Equal([10, 11, 12], q.Months);
    }

    // Real quarter from date
    [Fact]
    public void Real_ForJanuaryDate_ReturnsQ1()
    {
        var date = new DateOnly(2026, 1, 15);
        var q = Quarter.Real(date);
        Assert.Equal(1, q.Number);
        Assert.Equal(2026, q.Year);
    }

    [Fact]
    public void Real_ForAprilDate_ReturnsQ2()
    {
        var date = new DateOnly(2026, 4, 1);
        var q = Quarter.Real(date);
        Assert.Equal(2, q.Number);
    }

    [Fact]
    public void Real_ForJulyDate_ReturnsQ3()
    {
        var date = new DateOnly(2026, 7, 1);
        var q = Quarter.Real(date);
        Assert.Equal(3, q.Number);
    }

    [Fact]
    public void Real_ForOctoberDate_ReturnsQ4()
    {
        var date = new DateOnly(2026, 10, 1);
        var q = Quarter.Real(date);
        Assert.Equal(4, q.Number);
    }

    // ExcelQuarterFor equivalence classes (section 12 of architecture doc)

    // Case 1: same year, triF <= triU (included in T)
    [Fact]
    public void ExcelQuarterFor_SameYearInvoiceDateBeforeEnd_ReturnsThisQuarter()
    {
        // Q2 2026 range: [01-jan-2026, 30-jun-2026]
        var q = new Quarter(2026, 2);
        var invoiceDate = new DateOnly(2026, 3, 15);  // March 2026 - within range

        var result = q.ExcelQuarterFor(invoiceDate);

        Assert.NotNull(result);
        Assert.Equal(2026, result.Value.Year);
        Assert.Equal(2, result.Value.Number);
    }

    // Case 2: triF > triU (excluded)
    [Fact]
    public void ExcelQuarterFor_InvoiceDateAfterQuarterEnd_ReturnsNull()
    {
        // Q2 2026 range ends 30-jun-2026
        var q = new Quarter(2026, 2);
        var invoiceDate = new DateOnly(2026, 7, 1);  // July 2026 - after range end

        var result = q.ExcelQuarterFor(invoiceDate);

        Assert.Null(result);
    }

    // Case 3: 4T previous year with 1T (included) - example from doc: 15/12/2025 with 1T 2026
    [Fact]
    public void ExcelQuarterFor_PreviousYearQ4Date_With1TCurrentYear_ReturnsCurrentQuarter()
    {
        // Q1 2026 range: [01-oct-2025, 31-mar-2026]
        var q = new Quarter(2026, 1);
        var invoiceDate = new DateOnly(2025, 12, 15);  // 15/12/2025 → included in 1T 2026

        var result = q.ExcelQuarterFor(invoiceDate);

        Assert.NotNull(result);
        Assert.Equal(2026, result.Value.Year);
        Assert.Equal(1, result.Value.Number);
    }

    // Case 4: 4T previous year with 2T (excluded)
    [Fact]
    public void ExcelQuarterFor_PreviousYearQ4Date_With2TCurrentYear_ReturnsNull()
    {
        // Q2 2026 range: [01-jan-2026, 30-jun-2026]
        var q = new Quarter(2026, 2);
        var invoiceDate = new DateOnly(2025, 12, 15);  // 15/12/2025 → before 01-jan-2026, excluded

        var result = q.ExcelQuarterFor(invoiceDate);

        Assert.Null(result);
    }

    // Case 5: older year (excluded)
    [Fact]
    public void ExcelQuarterFor_OlderYearDate_ReturnsNull()
    {
        // Q1 2026 range: [01-oct-2025, 31-mar-2026]
        var q = new Quarter(2026, 1);
        var invoiceDate = new DateOnly(2024, 12, 15);  // 2024 - too old, before 01-oct-2025

        var result = q.ExcelQuarterFor(invoiceDate);

        Assert.Null(result);
    }

    // Case 6: same year, included boundary check
    [Fact]
    public void ExcelQuarterFor_InvoiceDateAtStartOfRange_ReturnsThisQuarter()
    {
        // Q1 2026 range starts: 01-oct-2025
        var q = new Quarter(2026, 1);
        var invoiceDate = new DateOnly(2025, 10, 1);  // exact start boundary

        var result = q.ExcelQuarterFor(invoiceDate);

        Assert.NotNull(result);
        Assert.Equal(1, result.Value.Number);
    }

    [Fact]
    public void ToString_ReturnsYearDashQNumber()
    {
        var q = new Quarter(2026, 1);
        Assert.Equal("2026-Q1", q.ToString());
    }
}
