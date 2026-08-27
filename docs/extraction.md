# Extraction

How invoice data gets out of a PDF, and why the provider is a configuration value rather than
a code change. See also [how it works](how-it-works.md) for what happens to the result.

## Pluggable extraction

Extraction sits behind a single port, `IInvoiceDataExtractor`. Two adapters are live, and which one is bound is a **configuration** value (`Extraction:Provider`), not a code change:

- **Local templates** (`TemplateInvoiceExtractor`) — **the default**. Reads the PDF's text layer with [PdfPig](https://github.com/UglyToad/PdfPig), identifies the supplier from anchor strings, and pulls each field with an anchor + regex defined in `appsettings.json`. No network, no credentials, **0 € per invoice**.
- **Google Document AI** (`GoogleDocumentAiExtractor`) — the cloud Invoice Parser. Generalises to layouts nobody has ever taught it, at roughly $0.10 per page and a service-account key.

This project started on LlamaParse, moved to Google Document AI when its field accuracy fell short, and ended up on a local template extractor that beat both on the invoices it actually sees. Across all three swaps, **the domain, the use cases and the pipeline tests never changed** — they depend on the port, not on the provider. That is the whole return on the hexagon, and it is why the default is now the free one.

The trade-off is honest: templates only know the suppliers they have been taught. When no template matches, the extractor says so (`RequiresManualEntry`) instead of inventing an invoice.

### OCR fallback

Scans have no text layer, so there is nothing for PdfPig to read. When `TemplateExtractor:OcrFallback:Enabled` is on, `TesseractOcrExtractor` rasterises the first page with poppler and reads it with Tesseract; the recovered text then goes through the same template pipeline. When it is off, a `NullOcrTextExtractor` returns an empty string, so no branch in the extractor has to know whether OCR exists. Any OCR failure — tools missing, timeout, corrupt PDF — degrades to manual entry rather than to an exception.

### Writing a template

Three commands, no Worker required:

```bash
dotnet run --project src/InvoiceProcessor.Worker -- dump-text invoice.pdf
dotnet run --project src/InvoiceProcessor.Worker -- template-check ./data/samples
dotnet run --project src/InvoiceProcessor.Worker -- make-samples          # regenerate the demo PDFs
```

`template-check` reports one line per PDF and a success ratio:

```
[OK]     aurora-A-2026-0148.pdf — Suministros Aurora S.L.
[NONE]   desconocido-TD-2026-77.pdf — no template matched
[SCAN]   escaneo-sin-texto.pdf — no text layer (0 chars)

Result: 3/5 coherent (60%)  ·  0 incoherent [BAD]
```

It does not stop at "every required field was captured" — it cross-checks `net + tax = total`, so `[OK]` means the numbers are sane rather than merely present. A greedy pattern that grabs a phone number as the tax amount shows up as `[BAD]`.
