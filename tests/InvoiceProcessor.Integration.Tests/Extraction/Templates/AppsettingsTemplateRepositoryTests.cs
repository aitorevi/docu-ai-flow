using InvoiceProcessor.Infrastructure.Extraction.Templates;
using Microsoft.Extensions.Options;

namespace InvoiceProcessor.Integration.Tests.Extraction.Templates;

// The repository makes AMOUNT patterns (net/tax/total_amount) tolerant of a leading minus sign at
// load time, in memory, so credit notes (negative amounts) are captured without editing
// each template. Non-amount fields and the "[\s\S]" any-char idiom must stay untouched.
public sealed class AppsettingsTemplateRepositoryTests
{
    // ── AllowNegativeAmounts (the transform) ─────────────────────────────────

    [Theory]
    // Every NUMERIC character class gets a "-?" prefix (including columns the pattern skips).
    [InlineData(@"[\s\S]*?\n[\d.,]+\s+([\d.,]+)", @"[\s\S]*?\n-?[\d.,]+\s+(-?[\d.,]+)")]
    [InlineData(@"([\d.,]+)", @"(-?[\d.,]+)")]
    [InlineData(@"([\d.]+)", @"(-?[\d.]+)")]
    [InlineData(@"([\d,]+)", @"(-?[\d,]+)")]
    // Spaced-digit class — still numeric.
    [InlineData(@"[.\s]*([\d.,\s]+?)\s*\n", @"[.\s]*(-?[\d.,\s]+?)\s*\n")]
    public void AllowNegativeAmounts_PrefixesNumericClasses(string input, string expected)
    {
        Assert.Equal(expected, AppsettingsTemplateRepository.AllowNegativeAmounts(input));
    }

    [Theory]
    // The "any char" idiom must NOT be treated as a number.
    [InlineData(@"[\s\S]*?\|\s*FACTURA\s+([\d.,]+)", @"[\s\S]*?\|\s*FACTURA\s+(-?[\d.,]+)")]
    // Non-numeric classes stay untouched; \S+ already accepts the sign.
    [InlineData(@".+:\s*(\S+)", @".+:\s*(\S+)")]
    [InlineData(@"[^)]*\)[.\s]*(\S+)", @"[^)]*\)[.\s]*(\S+)")]
    public void AllowNegativeAmounts_LeavesNonNumericTokensAlone(string input, string expected)
    {
        Assert.Equal(expected, AppsettingsTemplateRepository.AllowNegativeAmounts(input));
    }

    [Fact]
    public void AllowNegativeAmounts_IsIdempotent()
    {
        const string already = @"[\s\S]*?\n-?[\d.,]+\s+(-?[\d.,]+)";
        Assert.Equal(already, AppsettingsTemplateRepository.AllowNegativeAmounts(already));
    }

    // ── Repository wiring: only amount fields are transformed ─────────────────

    [Fact]
    public void Repository_SignsAmountFields_ButNotOthers()
    {
        var opts = new TemplateExtractorOptions
        {
            Templates =
            [
                new TemplateEntryOptions
                {
                    SupplierId = "acme",
                    IdentificationAnchors = ["ACME"],
                    Fields = new()
                    {
                        ["invoice_number"] = new FieldEntryOptions { Anchors = ["Nº"], Pattern = @"([\d.,]+)" },
                        ["net_amount"]     = new FieldEntryOptions { Anchors = ["Base"], Pattern = @"([\d.,]+)" },
                        ["tax_amount"]     = new FieldEntryOptions { Anchors = ["IVA"], Pattern = @"([\d.,]+)" },
                        ["total_amount"]   = new FieldEntryOptions { Anchors = ["Total"], Pattern = @"([\d.,]+)" },
                    },
                },
            ],
        };

        var repo = new AppsettingsTemplateRepository(Options.Create(opts));
        var fields = repo.GetAll().Single().Fields;

        Assert.Equal(@"(-?[\d.,]+)", fields["net_amount"].Pattern);
        Assert.Equal(@"(-?[\d.,]+)", fields["tax_amount"].Pattern);
        Assert.Equal(@"(-?[\d.,]+)", fields["total_amount"].Pattern);
        // invoice_number is not an amount → left exactly as authored.
        Assert.Equal(@"([\d.,]+)", fields["invoice_number"].Pattern);
    }
}
