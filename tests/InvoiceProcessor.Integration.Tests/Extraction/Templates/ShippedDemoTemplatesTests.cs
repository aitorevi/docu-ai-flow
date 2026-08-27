using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Domain.Documents;
using InvoiceProcessor.Infrastructure.Extraction.Ocr;
using InvoiceProcessor.Infrastructure.Extraction.Templates;
using InvoiceProcessor.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace InvoiceProcessor.Integration.Tests.Extraction.Templates;

// The demo templates that ship in the Worker's appsettings.json are the first thing anyone who
// clones this repo runs. They are exercised here against generated PDFs so a broken anchor or
// pattern fails the build, not the newcomer's first run.
//
// Each demo supplier deliberately uses a different layout: label:value, a column table, and
// dot leaders with US number and English date formats.
public sealed class ShippedDemoTemplatesTests
{
    private static readonly TemplateExtractorOptions ShippedOptions = LoadShippedOptions();

    private static TemplateExtractorOptions LoadShippedOptions()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "worker-appsettings.json");
        var configuration = new ConfigurationBuilder().AddJsonFile(path).Build();
        var options = new TemplateExtractorOptions();
        configuration.GetSection("TemplateExtractor").Bind(options);
        return options;
    }

    private static IInvoiceDataExtractor BuildExtractor()
    {
        var repo = new AppsettingsTemplateRepository(Options.Create(ShippedOptions));
        return new TemplateInvoiceExtractor(
            Options.Create(ShippedOptions), repo, new NullOcrTextExtractor());
    }

    // Rendered exactly the way `dotnet run -- make-samples` renders the shipped sample PDFs,
    // so these assertions cover the files a newcomer actually drops into the inbox.
    private static DocumentContent ToContent(string text) =>
        new("invoice.pdf", "application/pdf", new MemoryStream(SampleInvoices.Render(text)));

    [Fact]
    public void ShippedConfiguration_DefinesTheThreeDemoTemplates()
    {
        Assert.Equal(3, ShippedOptions.Templates.Length);
        Assert.Equal(
            ["aurora", "boreal", "cronos"],
            ShippedOptions.Templates.Select(t => t.SupplierId).Order());
    }

    // ── Layout 1: label: value ────────────────────────────────────────────────

    [Fact]
    public async Task AuroraTemplate_ExtractsEveryField()
    {
        var result = await BuildExtractor().ExtractAsync(ToContent(SampleInvoices.AuroraText), CancellationToken.None);

        Assert.False(result.RequiresManualEntry);
        Assert.Equal(1m, result.OverallConfidence);
        Assert.Equal("Suministros Aurora S.L.", result.Fields["supplier_name"].Value);
        Assert.Equal("B12345674", result.Fields["supplier_tax_id"].Value);
        Assert.Equal("A-2026/0148", result.Fields["invoice_number"].Value);
        Assert.Equal("2026-01-12", result.Fields["issue_date"].Value);
        Assert.Equal("2026-02-11", result.Fields["due_date"].Value);
        Assert.Equal("1240.50", result.Fields["net_amount"].Value);
        Assert.Equal("260.51", result.Fields["tax_amount"].Value);
        Assert.Equal("1501.01", result.Fields["total_amount"].Value);
    }

    // ── Layout 2: column table ────────────────────────────────────────────────

    [Fact]
    public async Task BorealTemplate_ExtractsFieldsFromAColumnLayout()
    {
        var result = await BuildExtractor().ExtractAsync(ToContent(SampleInvoices.BorealText), CancellationToken.None);

        Assert.False(result.RequiresManualEntry);
        Assert.Equal("Energía Boreal S.A.", result.Fields["supplier_name"].Value);
        Assert.Equal("FR-2026-0092", result.Fields["invoice_number"].Value);
        Assert.Equal("2026-02-05", result.Fields["issue_date"].Value);
        Assert.Equal("2026-03-07", result.Fields["due_date"].Value);
        Assert.Equal("980.00", result.Fields["net_amount"].Value);
        Assert.Equal("205.80", result.Fields["tax_amount"].Value);
        Assert.Equal("1185.80", result.Fields["total_amount"].Value);
    }

    // ── Layout 3: dot leaders, US numbers, English dates ──────────────────────

    [Fact]
    public async Task CronosTemplate_ExtractsUsNumbersAndEnglishDates()
    {
        var result = await BuildExtractor().ExtractAsync(ToContent(SampleInvoices.CronosText), CancellationToken.None);

        Assert.False(result.RequiresManualEntry);
        Assert.Equal("Papelería Cronos S.L.U.", result.Fields["supplier_name"].Value);
        Assert.Equal("C/26/0311", result.Fields["invoice_number"].Value);
        Assert.Equal("2026-02-18", result.Fields["issue_date"].Value);
        Assert.Equal("1320.00", result.Fields["net_amount"].Value);
        Assert.Equal("277.20", result.Fields["tax_amount"].Value);
        Assert.Equal("1597.20", result.Fields["total_amount"].Value);
    }

    // ── The three amounts must cross-check on every demo invoice ──────────────

    [Theory]
    [InlineData("1.240,50", "260,51", "1.501,01")]
    [InlineData("980,00", "205,80", "1.185,80")]
    [InlineData("1,320.00", "277.20", "1,597.20")]
    public void EveryDemoInvoice_HasCoherentAmounts(string net, string tax, string total)
    {
        Assert.Equal(CoherenceKind.Coherent,
            TemplateFieldParser.CheckAmountCoherence(net, tax, total).Kind);
    }

    // ── Every shipped sample behaves as advertised ────────────────────────────

    public static TheoryData<string> SampleFileNames()
    {
        var data = new TheoryData<string>();
        foreach (var sample in SampleInvoices.All) data.Add(sample.FileName);
        return data;
    }

    // A broken sample should fail the build, not someone's first run. This walks every file
    // `make-samples` writes through the shipped templates and checks it does what the demo
    // promises — including the extra Aurora and Boreal invoices, which exist so a supplier can
    // actually be seen earning its autonomy.
    [Theory]
    [MemberData(nameof(SampleFileNames))]
    public async Task EveryShippedSample_BehavesAsAdvertised(string fileName)
    {
        var sample = SampleInvoices.All.Single(s => s.FileName == fileName);

        var result = await BuildExtractor().ExtractAsync(ToContent(sample.Text), CancellationToken.None);

        Assert.Equal(sample.Extracts, !result.RequiresManualEntry);
        if (!sample.Extracts) return;

        // An extracted sample must also add up, or it would be rejected before ever reaching review.
        Assert.Equal(CoherenceKind.Coherent, TemplateFieldParser.CheckAmountCoherence(
            result.Fields["net_amount"].Value,
            result.Fields["tax_amount"].Value,
            result.Fields["total_amount"].Value).Kind);
    }

    [Fact]
    public void TheSamples_IncludeEnoughFromOneSupplierToEarnTrust()
    {
        // Three invoices from the same supplier is what makes the headline idea reachable in a
        // demo. If this ever drops below the shipped threshold, the demo stops telling the story.
        var auroraCount = SampleInvoices.All.Count(s => s.Text.Contains("SUMINISTROS AURORA"));
        Assert.True(auroraCount >= 3, $"Aurora aparece {auroraCount} vez/veces; hacen falta al menos 3.");
    }

    // ── A supplier with no template must not be guessed at ────────────────────

    [Fact]
    public async Task UnknownSupplier_RequiresManualEntry()
    {
        var result = await BuildExtractor().ExtractAsync(ToContent(SampleInvoices.UnknownSupplierText), CancellationToken.None);

        Assert.True(result.RequiresManualEntry);
    }
}
