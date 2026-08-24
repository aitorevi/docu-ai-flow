using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Domain.Documents;
using InvoiceProcessor.Infrastructure.Extraction.Ocr;
using InvoiceProcessor.Infrastructure.Extraction.Templates;
using InvoiceProcessor.Integration.Tests.Fixtures;
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

    private static DocumentContent ToContent(string text) =>
        new("invoice.pdf", "application/pdf", new MemoryStream(SyntheticPdf.WithText(text)));

    [Fact]
    public void ShippedConfiguration_DefinesTheThreeDemoTemplates()
    {
        Assert.Equal(3, ShippedOptions.Templates.Length);
        Assert.Equal(
            ["aurora", "boreal", "cronos"],
            ShippedOptions.Templates.Select(t => t.SupplierId).Order());
    }

    // ── Layout 1: label: value ────────────────────────────────────────────────

    public const string AuroraInvoice =
        "SUMINISTROS AURORA S.L.\n" +
        "CIF: B12345674\n" +
        "Factura Nº: A-2026/0148\n" +
        "Fecha: 12/01/2026\n" +
        "Vencimiento: 11/02/2026\n" +
        "Base imponible: 1.240,50\n" +
        "IVA 21%: 260,51\n" +
        "Total factura: 1.501,01\n";

    [Fact]
    public async Task AuroraTemplate_ExtractsEveryField()
    {
        var result = await BuildExtractor().ExtractAsync(ToContent(AuroraInvoice), CancellationToken.None);

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

    public const string BorealInvoice =
        "ENERGIA BOREAL S.A.\n" +
        "A87654321\n" +
        "Documento Fecha Vencimiento\n" +
        "FR-2026-0092 05/02/2026 07/03/2026\n" +
        "BASE IVA TOTAL\n" +
        "980,00 205,80 1.185,80\n";

    [Fact]
    public async Task BorealTemplate_ExtractsFieldsFromAColumnLayout()
    {
        var result = await BuildExtractor().ExtractAsync(ToContent(BorealInvoice), CancellationToken.None);

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

    public const string CronosInvoice =
        "PAPELERIA CRONOS S.L.U.\n" +
        "NIF B55512345\n" +
        "Invoice no. ....... C/26/0311\n" +
        "Issue date ........ Feb 18, 2026\n" +
        "Net .............. 1,320.00\n" +
        "VAT (21%) ........ 277.20\n" +
        "Amount due ....... 1,597.20\n";

    [Fact]
    public async Task CronosTemplate_ExtractsUsNumbersAndEnglishDates()
    {
        var result = await BuildExtractor().ExtractAsync(ToContent(CronosInvoice), CancellationToken.None);

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

    // ── A supplier with no template must not be guessed at ────────────────────

    [Fact]
    public async Task UnknownSupplier_RequiresManualEntry()
    {
        const string unknown =
            "TRANSPORTES DESCONOCIDOS S.L.\n" +
            "CIF B99999999\n" +
            "Numero de factura: TD-2026-77\n" +
            "Fecha de emision: 03/03/2026\n" +
            "Importe total: 450,00\n";

        var result = await BuildExtractor().ExtractAsync(ToContent(unknown), CancellationToken.None);

        Assert.True(result.RequiresManualEntry);
    }
}
