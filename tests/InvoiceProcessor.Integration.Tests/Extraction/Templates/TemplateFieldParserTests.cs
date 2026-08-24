using InvoiceProcessor.Infrastructure.Extraction.Templates;

namespace InvoiceProcessor.Integration.Tests.Extraction.Templates;

// Every case below is a real invoice layout that broke the parser at some point. They are kept
// as a regression suite: suppliers do not agree on how to print a number or a date, and each
// disagreement costs one branch here.
public sealed class TemplateFieldParserTests
{
    // ── ParseSpanishNumber ───────────────────────────────────────────────────

    [Theory]
    // Spanish format: "." groups thousands, "," is the decimal separator.
    [InlineData("1.210,00", 1210.00)]
    [InlineData("100,00", 100.00)]
    [InlineData("1.000.000,50", 1000000.50)]
    [InlineData("21,00", 21.00)]
    [InlineData("0,00", 0.00)]
    // US/English format (period = decimal, comma = thousands).
    [InlineData("237.74", 237.74)]
    [InlineData("1,132.11", 1132.11)]
    [InlineData("1,369.85", 1369.85)]
    // Digits spaced apart character-by-character by the PDF's text layer ("3 6 , 8 9 0").
    [InlineData("3 6 , 8 9 0", 36.89)]
    [InlineData("7 , 7 5 0", 7.75)]
    // Dot used as BOTH thousands and decimal separator ("1.170.00" meaning 1170.00).
    [InlineData("1.170.00", 1170.00)]
    [InlineData("1.415.70", 1415.70)]
    // Soft hyphen (U+00AD) / unicode minus (U+2212) used as the minus sign on credit notes.
    [InlineData("­24,80", -24.80)]
    [InlineData("−30,01", -30.01)]
    public void ParseSpanishNumber_ValidInput_ReturnsDecimal(string input, double expected)
    {
        var result = TemplateFieldParser.ParseSpanishNumber(input);
        Assert.NotNull(result);
        Assert.Equal((decimal)expected, result.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not-a-number")]
    public void ParseSpanishNumber_InvalidInput_ReturnsNull(string? input)
    {
        Assert.Null(TemplateFieldParser.ParseSpanishNumber(input));
    }

    // ── ParseDate ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("15/03/2025", "2025-03-15")]
    [InlineData("01/01/2026", "2026-01-01")]
    [InlineData("3 de marzo de 2025", "2025-03-03")]
    [InlineData("15 de enero de 2026", "2026-01-15")]
    [InlineData("2025-12-31", "2025-12-31")]
    // Two-digit year (dd/MM/yy).
    [InlineData("30/09/25", "2025-09-30")]
    [InlineData("22/01/26", "2026-01-22")]
    // Period-separated date (dd.MM.yyyy and dd.MM.yy).
    [InlineData("28.11.2025", "2025-11-28")]
    [InlineData("01.03.2026", "2026-03-01")]
    [InlineData("17.11.25", "2025-11-17")]
    [InlineData("26.03.26", "2026-03-26")]
    // Dash-separated date (dd-MM-yyyy and dd-MM-yy).
    [InlineData("25-11-2025", "2025-11-25")]
    [InlineData("17-12-2025", "2025-12-17")]
    [InlineData("12-12-25", "2025-12-12")]
    [InlineData("16-01-26", "2026-01-16")]
    // Abbreviated Spanish month (DD mon YYYY / DD mon. YYYY).
    [InlineData("31 jul 2025", "2025-07-31")]
    [InlineData("30 nov 2025", "2025-11-30")]
    [InlineData("15 ene 2026", "2026-01-15")]
    [InlineData("17 dic. 2025", "2025-12-17")]
    // Some issuers print the 4-char "sept" for September instead of the standard "sep".
    [InlineData("30 sept 2025", "2025-09-30")]
    [InlineData("1 sept 2025", "2025-09-01")]
    // English abbreviated month "Mon DD, YYYY" — foreign suppliers invoicing in English.
    [InlineData("Jan 21, 2026", "2026-01-21")]
    [InlineData("May 15, 2026", "2026-05-15")]
    // Single-digit day/month (d/M/yyyy and d/M/yy).
    [InlineData("6/5/2026", "2026-05-06")]
    [InlineData("6/5/26", "2026-05-06")]
    // Spaces around the slash ("30 / 9 / 2025").
    [InlineData("30 / 9 / 2025", "2025-09-30")]
    [InlineData("30 / 10 / 2025", "2025-10-30")]
    public void ParseDate_ValidInput_ReturnsIso(string input, string expected)
    {
        Assert.Equal(expected, TemplateFieldParser.ParseDate(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not-a-date")]
    public void ParseDate_InvalidInput_ReturnsNull(string? input)
    {
        Assert.Null(TemplateFieldParser.ParseDate(input));
    }

    // ── FindAnchorAndCapture ──────────────────────────────────────────────────

    [Fact]
    public void FindAnchorAndCapture_LongerAnchorWinsOverShorterEvenIfShorterAppearFirst()
    {
        // The text has "Total" (short anchor at pos 0) and "Total a pagar" (long anchor at pos 20).
        // The algorithm must try longest-first, so "Total a pagar" wins.
        const string text = "Total: 50,00 EUR  — Total a pagar: 121,00 EUR";
        var anchors = new[] { "Total", "Total a pagar" };    // short before long in input
        const string pattern = @":\s*([\d.,]+)";

        var result = TemplateFieldParser.FindAnchorAndCapture(text, anchors, pattern);

        // "Total a pagar" anchor is longer → must be tried first → captures 121,00
        Assert.Equal("121,00", result);
    }

    [Fact]
    public void FindAnchorAndCapture_ReturnsNullWhenNoAnchorMatches()
    {
        var result = TemplateFieldParser.FindAnchorAndCapture(
            "Some text without anchors",
            ["MISSING ANCHOR"],
            @":\s*([\d.,]+)");

        Assert.Null(result);
    }

    [Fact]
    public void FindAnchorAndCapture_ReturnsNullWhenPatternDoesNotMatchAfterAnchor()
    {
        var result = TemplateFieldParser.FindAnchorAndCapture(
            "Base imponible sin número aquí",
            ["Base imponible"],
            @":\s*([\d.,]+)");

        Assert.Null(result);
    }

    [Fact]
    public void FindAnchorAndCapture_CapturesFirstGroupAfterAnchor()
    {
        const string text = "Nº Factura: F-2025-001 Fecha: 01/03/2025";
        var result = TemplateFieldParser.FindAnchorAndCapture(
            text, ["Nº Factura"], @":\s*(\S+)");

        Assert.Equal("F-2025-001", result);
    }

    [Fact]
    public void FindAnchorAndCapture_DefaultsToFirstAnchorOccurrence()
    {
        // "Base" repeats (per-page header); default behaviour anchors on the FIRST one.
        const string text = "Base\n10,00\nBase\n999,00";

        var result = TemplateFieldParser.FindAnchorAndCapture(text, ["Base"], @"\s*([\d.,]+)");

        Assert.Equal("10,00", result);
    }

    [Fact]
    public void FindAnchorAndCapture_MatchLastAnchor_CapturesAfterTheLastOccurrence()
    {
        // On a multi-page invoice the amount label repeats per page and the totals sit after the
        // LAST occurrence. matchLastAnchor:true captures there instead of on page 1's line item.
        const string text = "Base\n10,00\n--- pág. 2 ---\nBase\n999,00";

        var result = TemplateFieldParser.FindAnchorAndCapture(
            text, ["Base"], @"\s*([\d.,]+)", matchLastAnchor: true);

        Assert.Equal("999,00", result);
    }

    // ── CheckAmountCoherence ─────────────────────────────────────────────────

    [Fact]
    public void CheckAmountCoherence_AllPresentAndCoherent_ReturnsCoherent()
    {
        // 1.210,00 + 254,10 = 1.464,10 (exact match)
        var result = TemplateFieldParser.CheckAmountCoherence("1.210,00", "254,10", "1.464,10");
        Assert.Equal(CoherenceKind.Coherent, result.Kind);
    }

    [Fact]
    public void CheckAmountCoherence_DiffWithinTolerance_ReturnsCoherent()
    {
        // net + tax = 100.00 + 21.00 = 121.00; total = 121.04 → diff = 0.04 ≤ 0.05
        var result = TemplateFieldParser.CheckAmountCoherence("100,00", "21,00", "121,04");
        Assert.Equal(CoherenceKind.Coherent, result.Kind);
    }

    [Fact]
    public void CheckAmountCoherence_DiffExceedsTolerance_ReturnsIncoherent()
    {
        // net + tax = 100,00 + 21,00 = 121,00; total = 200,00 → clearly wrong
        var result = TemplateFieldParser.CheckAmountCoherence("100,00", "21,00", "200,00");
        Assert.Equal(CoherenceKind.Incoherent, result.Kind);
        Assert.Equal(100m, result.Net);
        Assert.Equal(21m, result.Tax);
        Assert.Equal(200m, result.Total);
        Assert.Equal(79m, result.Diff);
    }

    [Theory]
    [InlineData(null, "21,00", "121,00")]
    [InlineData("100,00", null, "121,00")]
    [InlineData("100,00", "21,00", null)]
    public void CheckAmountCoherence_AnyAmountMissing_ReturnsUncrossable(
        string? net, string? tax, string? total)
    {
        var result = TemplateFieldParser.CheckAmountCoherence(net, tax, total);
        Assert.Equal(CoherenceKind.Uncrossable, result.Kind);
    }

    [Fact]
    public void CheckAmountCoherence_TaxIdCapturedAsAmount_ReturnsIncoherent()
    {
        // The classic template bug: a greedy pattern captures the supplier's phone or tax id
        // as tax_amount. The coherence cross-check is what catches it.
        var result = TemplateFieldParser.CheckAmountCoherence("1.500,00", "961490317", "1.815,00");
        Assert.Equal(CoherenceKind.Incoherent, result.Kind);
    }
}
