using ClosedXML.Excel;
using InvoiceProcessor.Application.Invoices;
using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Domain.Dispatch;
using InvoiceProcessor.Infrastructure.Files;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace InvoiceProcessor.Infrastructure.Export;

public sealed class ClosedXmlQuarterSpreadsheetExporter(IOptions<FolderOptions> folders) : IQuarterSpreadsheetExporter
{
    private readonly string _output = Path.GetFullPath(folders.Value.Output);
    private static readonly string[] Headers =
        ["CIF", "FechaFactura", "Trimestre", "Año", "FechaVto", "NumFactura", "FechaPago", "Base", "ComPaypal"];

    public Task<string> ExportAsync(Quarter quarter, IReadOnlyCollection<StoredInvoice> invoices, CancellationToken ct)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Facturas");

        for (var c = 0; c < Headers.Length; c++)
            ws.Cell(1, c + 1).Value = Headers[c];

        var row = 2;
        foreach (var inv in invoices)
        {
            ws.Cell(row, 1).Value = inv.SupplierTaxId ?? "";
            ws.Cell(row, 2).Value = inv.IssueDate.ToString("dd/MM/yyyy");
            ws.Cell(row, 3).Value = quarter.Number.ToString();
            ws.Cell(row, 4).Value = quarter.Year.ToString();
            ws.Cell(row, 5).Value = inv.DueDate?.ToString("dd/MM/yyyy") ?? "";
            ws.Cell(row, 6).Value = inv.InvoiceNumber;
            ws.Cell(row, 7).Value = "";
            ws.Cell(row, 8).Value = inv.NetAmount.ToString("0.00", CultureInfo.InvariantCulture);
            ws.Cell(row, 9).Value = "";
            row++;
        }

        ws.CellsUsed().Style.NumberFormat.Format = "@";

        Directory.CreateDirectory(_output);
        var path = Path.Combine(_output,
            $"facturas_extraidas_{quarter.Year}Q{quarter.Number}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
        wb.SaveAs(path);
        return Task.FromResult(path);
    }
}
