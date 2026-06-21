using ClosedXML.Excel;
using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Infrastructure.Files;
using Microsoft.Extensions.Options;

namespace InvoiceProcessor.Infrastructure.Export;

public sealed class ClosedXmlMasterSpreadsheetWriter(
    IProcessedInvoiceRepository repository,
    IOptions<FolderOptions> folders) : IMasterSpreadsheetWriter
{
    private readonly string _output = Path.GetFullPath(folders.Value.Output);
    private static readonly SemaphoreSlim FileLock = new(1, 1);
    private static readonly string[] Headers =
        ["CIF", "Proveedor", "NumFactura", "FechaFactura", "FechaVto", "TrimestreReal", "AñoReal",
         "TrimDeclarado", "AñoDeclarado", "Base", "IVA", "Total", "Moneda", "Archivo"];

    public async Task RebuildAsync(CancellationToken ct)
    {
        await FileLock.WaitAsync(ct);
        try
        {
            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("Facturas");
            for (var c = 0; c < Headers.Length; c++)
                ws.Cell(1, c + 1).Value = Headers[c];
            ws.Row(1).Style.Font.Bold = true;

            var row = 2;
            await foreach (var inv in repository.ListAllAsync(ct))
            {
                ws.Cell(row, 1).Value = inv.SupplierTaxId ?? "";
                ws.Cell(row, 2).Value = inv.SupplierName;
                ws.Cell(row, 3).Value = inv.InvoiceNumber;
                ws.Cell(row, 4).Value = inv.IssueDate.ToDateTime(TimeOnly.MinValue);
                if (inv.DueDate.HasValue)
                    ws.Cell(row, 5).Value = inv.DueDate.Value.ToDateTime(TimeOnly.MinValue);
                ws.Cell(row, 6).Value = inv.RealQuarter.Number;
                ws.Cell(row, 7).Value = inv.RealQuarter.Year;
                ws.Cell(row, 8).Value = inv.DeclaredQuarter?.ToString() ?? "Pendiente";
                ws.Cell(row, 9).Value = inv.DeclaredYear?.ToString() ?? "";
                ws.Cell(row, 10).Value = inv.NetAmount;
                ws.Cell(row, 11).Value = inv.TaxAmount;
                ws.Cell(row, 12).Value = inv.TotalAmount;
                ws.Cell(row, 13).Value = inv.Currency;
                ws.Cell(row, 14).Value = Path.GetFileName(inv.ArchivedPath);
                row++;
            }

            ws.Column(4).Style.DateFormat.Format = "dd/mm/yyyy";
            ws.Column(5).Style.DateFormat.Format = "dd/mm/yyyy";
            if (row > 2)
                ws.Range(2, 10, row - 1, 12).Style.NumberFormat.Format = "#,##0.00";

            Directory.CreateDirectory(_output);
            wb.SaveAs(Path.Combine(_output, "maestro_facturas.xlsx"));
        }
        finally { FileLock.Release(); }
    }
}
