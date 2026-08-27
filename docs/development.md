# Development

Running the project without Docker, the layout of the code, and how the suite is organised.

## Running it without Docker

## Windows, without Docker (recommended path: `C:\docu-ai-flow`)

Clone to a path without spaces, then run the setup script once:

```
git clone https://github.com/aitorevi/docu-ai-flow C:\docu-ai-flow
cd C:\docu-ai-flow
git config core.hooksPath .githooks
```

Right-click `setup.ps1` → **Run with PowerShell**. It will:
- Verify that .NET 10 SDK is installed (shows the download link if missing)
- Create the `data\inbox`, `data\pending`, `data\archive`, `data\duplicates`, `data\failed`, `data\output` folders
- Copy `.env.example` → `.env`

The `.env` is optional — without it the app uses the local extractor. See [Configuration](configuration.md). Double-click **`run.bat`** to start.

To update the app later: double-click **`update.bat`** (runs `git pull`), then `run.bat` again.

## macOS / Linux, without Docker

```bash
git clone https://github.com/aitorevi/docu-ai-flow
cd docu-ai-flow
git config core.hooksPath .githooks
dotnet restore
dotnet run --project src/InvoiceProcessor.Worker
```

`.env` is optional — copy `.env.example` only if you want Document AI, OCR or email.

## Rebuilding the frontend

The built site is committed under `src/InvoiceProcessor.Worker/wwwroot/`, so `dotnet run` works without Node. To change the UI:

```bash
cd frontend
npm install
npm run dev     # HMR at :4321, API proxied to the running Worker
npm run build   # rebuilds wwwroot
```

## CLI modes

All operations are also available as one-shot CLI commands:

```bash
# Export quarterly Excel
dotnet run --project src/InvoiceProcessor.Worker -- export 2026 1
# Output: ./data/output/facturas_extraidas_2026Q1_{timestamp}.xlsx

# Send quarterly PDFs to advisor
dotnet run --project src/InvoiceProcessor.Worker -- send 2026 1

# Rebuild master spreadsheet from database
dotnet run --project src/InvoiceProcessor.Worker -- master
```

The fiscal quarter rule (Sistema 2) is applied: Q1 2026 covers 01-Oct-2025 → 31-Mar-2026. All commands are idempotent — re-running only processes what is new since the last run.

**Large ZIP handling:** if the quarter's ZIP would exceed `MailDispatch:MaxAttachmentMb` (default 38 MB), the app automatically splits by month and sends one email per part (`Invoices 2026-Q1 - Part 1/3 (January)`). No manual intervention needed.

## Project structure

```
docu-ai-flow.sln
Directory.Build.props          # net10.0, Nullable, TreatWarningsAsErrors, SharpMonads.Core
Dockerfile                     # astro build → dotnet publish → aspnet + poppler/tesseract
docker-compose.yml             # port 8080, ./data bind-mounted, optional .env
src/
├── InvoiceProcessor.Domain/          # Zero external dependencies
│   ├── Invoices/                     # Invoice, Money, Supplier, SupplierTrust, InvoiceLine, InvoiceId
│   ├── Documents/                    # IncomingDocument, DocumentContent, DocumentId
│   └── Dispatch/                     # Quarter (fiscal rules: ExcelSourceRange, ExcelQuarterFor)
├── InvoiceProcessor.Application/     # Use cases + port definitions
│   ├── Ports/Inbound/                # IProcessInvoiceUseCase, IReviewInvoiceUseCase, ISendQuarterToAdvisorUseCase, IExportQuarterToSpreadsheetUseCase
│   ├── Ports/Outbound/               # IInvoiceDataExtractor, IDocumentArchiver, IPendingInvoiceRepository, ISupplierTrustRepository, …
│   ├── Invoices/                     # ProcessInvoiceService, ReviewInvoiceService, PendingInvoice, ExtractionToInvoiceMapper
│   ├── Dispatch/                     # SendQuarterToAdvisorService
│   └── Export/                       # ExportQuarterToSpreadsheetService
├── InvoiceProcessor.Infrastructure/  # Concrete adapters
│   ├── Extraction/Templates/         # TemplateInvoiceExtractor, TemplateFieldParser, AppsettingsTemplateRepository (default)
│   ├── Extraction/DocumentAi/        # GoogleDocumentAiExtractor, GoogleDocumentAiMapper, GoogleDocumentAiOptions
│   ├── Extraction/Ocr/               # TesseractOcrExtractor, NullOcrTextExtractor (fallback for scans)
│   ├── Extraction/                   # SupplierNameHeuristics (shared)
│   ├── Files/                        # FileSystemDocumentReader, FileSystemDocumentArchiver, FileSystemArchivedInvoiceSource, FileStabilityWaiter
│   ├── Suppliers/                    # CatalogSupplierNormalizer, CompanyOptions
│   ├── Idempotency/                  # JsonFileProcessedDocumentLog
│   ├── Persistence/                  # SqliteProcessedInvoiceRepository, SqlitePendingInvoiceRepository, SqliteSupplierTrustRepository, …
│   ├── Export/                       # ClosedXmlMasterSpreadsheetWriter, ClosedXmlQuarterSpreadsheetExporter
│   ├── Dispatch/                     # ZipInvoiceArchiveCompressor
│   └── Mail/                         # ResendAdvisorMailSender
└── InvoiceProcessor.Worker/          # Composition root + web UI + CLI entry points
    ├── Program.cs                    # DI wiring, health, export/send endpoints, CLI modes
    ├── ReviewEndpoints.cs            # /api/pending: queue, detail, PDF, confirm / reject / requeue
    ├── TemplateDiagnostics.cs        # template-check and dump-text
    ├── SampleInvoices.cs             # the fictional demo invoices + make-samples
    ├── FolderWatcherService.cs       # BackgroundService: watcher + polling + concurrency gate
    └── wwwroot/                      # Astro build output, served at http://localhost:5000
tests/
├── InvoiceProcessor.Domain.Tests/          # Pure unit tests (Money, Invoice, Quarter)
├── InvoiceProcessor.Application.Tests/    # Use cases with NSubstitute port doubles
└── InvoiceProcessor.Integration.Tests/    # Archiver, SQLite repos, Excel writers, zip, watcher stress, mapper golden-master
    ├── Api/                                # Endpoint tests against the real host (WebApplicationFactory)
    ├── Fixtures/                           # MinimalPdf, SyntheticPdf, FakeInvoiceDataExtractor
    ├── Extraction/                         # template extractor + parser, shipped demo templates, Document AI mapper golden-master
    └── Pipeline/                           # End-to-end pipeline tests (Processing, Review, Export, Send)
frontend/                          # Astro 6 — builds straight into the Worker's wwwroot
├── src/pages/index.astro          # Panel: export + send
├── src/pages/review.astro         # Review screen: PDF beside the form
└── src/layouts, components, scripts, styles
data/
├── samples/     # Fictional invoices to try it with (committed)
├── inbox/       # Drop PDFs here
├── pending/     # Held for human review — see /review
├── archive/     # Filed PDFs (year/month/supplier/supplier-number.pdf)
├── duplicates/  # Already seen: same bytes, or same invoice number + tax id
├── failed/      # Rejected, or extractions that did not add up
└── output/      # Generated Excel files
```

## Running tests

```bash
dotnet test                                  # all 250 tests
dotnet test --filter "Category!=LiveGcp&Category!=RequiresTesseract"   # CI default
dotnet test --filter "Domain"                # domain unit tests only
dotnet test --filter "Application"           # use-case unit tests only
dotnet test --filter "Integration"           # integration tests (SQLite, Excel, zip, watcher, pipeline, mappers)
dotnet test --filter "Pipeline"              # end-to-end pipeline tests only
```

Two tests are excluded from CI. `GoogleDocumentAiExtractorLiveTests` (`Category=LiveGcp`) calls the real Google API and self-skips unless the credentials and processor env vars are present. `TesseractOcrExtractorIntegrationTests` (`Category=RequiresTesseract`) shells out to the real poppler + tesseract binaries.

`ShippedDemoTemplatesTests` runs the demo templates from the Worker's own `appsettings.json` against generated PDFs, so a broken anchor or pattern fails the build rather than someone's first run.

### Test layers

| Layer | Count | What they cover |
|---|---|---|
| Domain | 28 | `Money`, `Invoice`, `Quarter`, `SupplierTrust` — pure business rules, no dependencies |
| Application | 45 | Use cases with NSubstitute port doubles — processing, review, trust, export, send |
| Integration | 177 | Real SQLite, ClosedXML, filesystem, zip, PdfPig; template extraction and the shipped demo templates; the API through `WebApplicationFactory`; WireMock stub for Resend |

The **Pipeline** tests (inside Integration) are the most comprehensive: they wire the full DI graph (`AddApplication + AddInfrastructure`) against temporary directories and a temporary SQLite database. Extraction is driven through the `IInvoiceDataExtractor` port via an in-memory `FakeInvoiceDataExtractor` (no HTTP, no provider coupling); a WireMock server stubs only Resend. They exercise the entire system end-to-end:

- **Processing** — happy path (PDF → extraction → DB → archive), duplicate detection, low-confidence rejection (moved to `failed/`), quarter assignment, multiple invoices from the same supplier
- **Export** — Excel generation, idempotency (re-running only exports what is new), master spreadsheet rebuild
- **Send** — ZIP creation, correct email recipient, sent log, idempotency, pending-only selection (previously sent PDFs are excluded)

No real API keys or network access are needed to run any test.

## Architecture

```
Worker ──► Infrastructure ──► Application ──► Domain
```

- **Domain** has zero external dependencies. All business rules live here.
- **Application** defines the ports (interfaces) and implements the use cases against them. It only depends on Domain.
- **Infrastructure** implements the ports using real technology (SQLite, ClosedXML, HttpClient, FileSystem). It depends on Application and Domain.
- **Worker** is the composition root. It wires everything together and is the only place where concrete adapters are chosen.

Architecture tests (NetArchTest) enforce these boundaries on every build.

## Development workflow

Every task starts with the orchestrator agent:

```
@orchestrator <describe the task>
```

The orchestrator explores, debates, plans, implements (TDD), reviews and validates. Plans live in `workflow/` and move through `plans/ → in-progress/ → reviewing/ → done/`.

Commit convention: `type(scope): short description in imperative English`

Types: `feat`, `fix`, `chore`, `docs`, `refactor`, `test`
