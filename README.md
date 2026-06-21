# docu-ai-flow

Folder-watching .NET 10 service that extracts invoice data from PDFs via AI, exports to Excel, and emails quarterly archives to your advisor.

Drop a PDF into `./data/inbox/`. The service detects it, extracts the invoice fields via [Google Document AI](https://cloud.google.com/document-ai), persists the data in SQLite, archives the original PDF, and regenerates a master Excel spreadsheet. When a quarter closes, one command ZIPs the PDFs and emails them to your tax advisor via [Resend](https://resend.com).

Built with **.NET 10**, **C#**, and a strict **hexagonal architecture**. Business errors use the **Result pattern** (`SharpMonads.Core`); infrastructure failures bubble up to Polly.

### Pluggable extraction

Extraction sits behind a single port, `IInvoiceDataExtractor`, so the AI provider is a swappable detail chosen in the composition root. Two adapters ship in the box:

- **Google Document AI** (`GoogleDocumentAiExtractor`) — the active extractor. Its Invoice Parser returns typed fields that survive arbitrary supplier layouts.
- **LlamaParse** (`LlamaParseExtractor`) — kept as a reference implementation. It is still registered as a type but no longer bound to the port; in our tests its field accuracy fell short, so it is preserved as a proof-of-result, not the default.

Swapping providers is a one-line change in `Program.cs`. No test, use case, or domain type depends on the concrete adapter.

## Stack

| Layer | Technology |
|---|---|
| Runtime | .NET 10 (ASP.NET Core + Worker Service) |
| Architecture | Hexagonal (ports & adapters) |
| Error handling | `Result<TValue, TError>` via SharpMonads.Core |
| Persistence | SQLite (`Microsoft.Data.Sqlite`) |
| Excel output | ClosedXML |
| AI extraction | Google Document AI (Invoice Parser); LlamaParse REST API kept as reference |
| Email | Resend REST API |
| Resilience | Polly (`AddStandardResilienceHandler`) |
| Tests | xUnit + NSubstitute + WireMock.Net |

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A [Google Cloud](https://cloud.google.com/document-ai) project with a Document AI **Invoice Parser** processor and a service-account JSON key (for invoice extraction). A [LlamaParse](https://llamaindex.ai) API key is only needed if you switch back to the reference adapter.
- A [Resend](https://resend.com) account and verified domain (for email dispatch)
- [Claude Code](https://claude.ai/code) (optional, for AI-assisted development)
- [gh](https://cli.github.com) (optional, for PR management)

## Setup

### Windows (recommended path: `C:\docu-ai-flow`)

Clone to a path without spaces, then run the setup script once:

```
git clone https://github.com/aitorevi/docu-ai-flow C:\docu-ai-flow
cd C:\docu-ai-flow
git config core.hooksPath .githooks
```

Right-click `setup.ps1` → **Run with PowerShell**. It will:
- Verify that .NET 10 SDK is installed (shows the download link if missing)
- Create the `data\inbox`, `data\archive`, `data\failed`, `data\output` folders
- Copy `.env.example` → `.env` and print instructions for filling in the API keys

Edit `.env` with your keys (see [Configuration](#configuration)), then double-click **`run.bat`** to start.

To update the app later: double-click **`update.bat`** (runs `git pull`), then `run.bat` again.

### macOS / Linux

```bash
git clone https://github.com/aitorevi/docu-ai-flow
cd docu-ai-flow
git config core.hooksPath .githooks
dotnet restore
cp .env.example .env   # fill in your API keys
dotnet run --project src/InvoiceProcessor.Worker
```

## Usage

### Web dashboard (default mode)

Starting the app opens a browser at `http://localhost:5000` with a dashboard for all operations.

```bash
# Windows: double-click run.bat
# macOS/Linux:
dotnet run --project src/InvoiceProcessor.Worker
```

Stop with **Ctrl+C**. If the port is already in use (e.g. a previous run crashed), free it first:

```bash
lsof -ti :5000 | xargs kill -9
```

Drop PDF invoices into `./data/inbox/`. The watcher will:
1. Detect the file (real-time watcher + polling fallback)
2. Wait until the file is fully written
3. Check for duplicates via SHA-256 hash
4. Extract invoice fields via Google Document AI
5. Normalize the supplier against the catalog in `appsettings.json`
6. Persist the invoice in SQLite (`./data/invoices.db`)
7. Archive the PDF to `./data/archive/{year}/{month}/{supplier}/`
8. Regenerate `./data/output/maestro_facturas.xlsx`

Failed invoices (extraction errors, incoherent totals) are moved to `./data/failed/`.

From the dashboard you can:
- **Export Excel** — generate the quarterly spreadsheet ready for your accounting app
- **Send to advisor** — ZIP and email the quarter's PDFs to your tax advisor (+ CC to yourself if configured)

### CLI modes

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
src/
├── InvoiceProcessor.Domain/          # Zero external dependencies
│   ├── Invoices/                     # Invoice, Money, Supplier, InvoiceLine, InvoiceId
│   ├── Documents/                    # IncomingDocument, DocumentContent, DocumentId
│   └── Dispatch/                     # Quarter (fiscal rules: ExcelSourceRange, ExcelQuarterFor)
├── InvoiceProcessor.Application/     # Use cases + port definitions
│   ├── Ports/Inbound/                # IProcessInvoiceUseCase, ISendQuarterToAdvisorUseCase, IExportQuarterToSpreadsheetUseCase
│   ├── Ports/Outbound/               # IInvoiceDataExtractor, IDocumentArchiver, IProcessedInvoiceRepository, …
│   ├── Invoices/                     # ProcessInvoiceService, ExtractionToInvoiceMapper, StoredInvoice
│   ├── Dispatch/                     # SendQuarterToAdvisorService
│   └── Export/                       # ExportQuarterToSpreadsheetService
├── InvoiceProcessor.Infrastructure/  # Concrete adapters
│   ├── Extraction/DocumentAi/        # GoogleDocumentAiExtractor, GoogleDocumentAiMapper, GoogleDocumentAiOptions (active)
│   ├── Extraction/LlamaParse/        # LlamaParseExtractor, LlamaParseMapper (reference / proof-of-result)
│   ├── Extraction/                   # SupplierNameHeuristics (shared)
│   ├── Files/                        # FileSystemDocumentReader, FileSystemDocumentArchiver, FileSystemArchivedInvoiceSource, FileStabilityWaiter
│   ├── Suppliers/                    # CatalogSupplierNormalizer, CompanyOptions
│   ├── Idempotency/                  # JsonFileProcessedDocumentLog
│   ├── Persistence/                  # SqliteProcessedInvoiceRepository, SqliteExportedInvoiceLog, SqliteSentInvoiceLog
│   ├── Export/                       # ClosedXmlMasterSpreadsheetWriter, ClosedXmlQuarterSpreadsheetExporter
│   ├── Dispatch/                     # ZipInvoiceArchiveCompressor
│   └── Mail/                         # ResendAdvisorMailSender
└── InvoiceProcessor.Worker/          # Composition root + web UI + CLI entry points
    ├── Program.cs                    # DI wiring, web endpoints, watch / export / send / master modes
    ├── FolderWatcherService.cs       # BackgroundService: watcher + polling + concurrency gate
    └── wwwroot/index.html            # Dashboard served at http://localhost:5000
tests/
├── InvoiceProcessor.Domain.Tests/          # Pure unit tests (Money, Invoice, Quarter)
├── InvoiceProcessor.Application.Tests/    # Use cases with NSubstitute port doubles
└── InvoiceProcessor.Integration.Tests/    # Archiver, SQLite repos, Excel writers, zip, watcher stress, mapper golden-master
    ├── Fixtures/                           # MinimalPdf, FakeInvoiceDataExtractor, RealInvoices/gcp/*.json (anonymized)
    ├── Extraction/                         # GoogleDocumentAiMapper golden-master, LlamaParse contract, Google live (skipped)
    └── Pipeline/                           # End-to-end pipeline tests (PipelineFixture, Processing, Export, Send)
data/
├── inbox/      # Drop PDFs here
├── archive/    # Processed PDFs (year/month/supplier/supplier-number.pdf)
├── failed/     # PDFs that could not be parsed
└── output/     # Generated Excel files
```

## Configuration

The recommended way to configure the app is via a `.env` file in the repo root (gitignored). Copy `.env.example` to get started:

```
# Google Document AI (active extractor). Credentials are loaded from the
# service-account JSON pointed to by GOOGLE_APPLICATION_CREDENTIALS (ADC).
GOOGLE_APPLICATION_CREDENTIALS=/path/to/service-account.json
GoogleDocumentAi__ProjectId=your-gcp-project-id
GoogleDocumentAi__Location=eu
GoogleDocumentAi__ProcessorId=your-processor-id
# Your own company identity — used to filter the buyer out of the extracted supplier fields.
Company__TaxId=
Company__Name=

# Only needed if you switch back to the LlamaParse reference adapter.
LlamaParse__ApiKey=llx-your-key-here

Resend__ApiKey=re_your-key-here
Resend__FromAddress=invoices@yourdomain.com
Resend__AdvisorAddress=advisor@accounting.com
# Optional: receive a copy of every email sent to the advisor
Resend__CcAddress=you@email.com
```

The double-underscore maps to JSON section separators (`Resend__ApiKey` → `Resend:ApiKey`). On macOS/Linux you can also use `dotnet user-secrets` instead. The Google service-account key is never read from config — only the path in `GOOGLE_APPLICATION_CREDENTIALS`.

All non-secret settings live in `src/InvoiceProcessor.Worker/appsettings.json`:

```json
{
  "Folders": {
    "Inbox": "./data/inbox",
    "Archive": "./data/archive",
    "Failed": "./data/failed",
    "Output": "./data/output",
    "MaxConcurrency": 3,
    "PollSeconds": 5
  },
  "GoogleDocumentAi": {
    "ProjectId": "",
    "Location": "eu",
    "ProcessorId": ""
  },
  "Company": {
    "TaxId": "",
    "Name": ""
  },
  "Extraction": {
    "ConfidenceThreshold": 0.6
  },
  "LlamaParse": {
    "ParseEndpoint": "https://api.cloud.llamaindex.ai/api/v1/parsing/upload",
    "ApiKey": ""
  },
  "SupplierCatalog": {
    "Suppliers": [
      { "CanonicalName": "Repsol", "TaxId": "A78374725", "Aliases": ["REPSOL S.A.", "Repsol Comercializadora"] },
      { "CanonicalName": "Endesa", "TaxId": "A81948077", "Aliases": ["Endesa Energía", "ENDESA ENERGIA S.A.U."] }
    ]
  },
  "Resend": {
    "ApiKey": "",
    "FromName": "Invoice Processor",
    "FromAddress": "invoices@yourdomain.com",
    "AdvisorAddress": "advisor@accounting.com",
    "CcAddress": "",
    "MaxAttachmentMb": 38
  },
  "MailDispatch": {
    "MaxAttachmentMb": 38
  },
  "Database": {
    "Path": "./data/invoices.db"
  }
}
```

| Key | Description |
|-----|-------------|
| `GoogleDocumentAi:Location` | Document AI region; must match the processor's location (e.g. `eu`, `us`) |
| `GoogleDocumentAi:ProcessorId` | The Invoice Parser processor id from the Google Cloud console |
| `Company:TaxId` / `Company:Name` | Your own identity — filtered out of the extracted supplier fields |
| `Resend:FromAddress` | Must belong to a domain verified in Resend (DKIM/SPF) |
| `Resend:CcAddress` | Optional. When set, a copy of every advisor email is sent here |
| `Extraction:ConfidenceThreshold` | PDFs with lower average confidence are moved to `failed/` |
| `MailDispatch:MaxAttachmentMb` | ZIP size limit before splitting by month (default 38 MB — Resend's hard limit is 40 MB) |

## Running tests

```bash
dotnet test                                  # all 111 tests
dotnet test --filter "Category!=LiveGcp"     # everything except the live Google smoke test (CI default)
dotnet test --filter "Domain"                # domain unit tests only
dotnet test --filter "Application"           # use-case unit tests only
dotnet test --filter "Integration"           # integration tests (SQLite, Excel, zip, watcher, pipeline, mappers)
dotnet test --filter "Pipeline"              # end-to-end pipeline tests only
```

One integration test (`GoogleDocumentAiExtractorLiveTests`, tagged `[Trait("Category","LiveGcp")]`) calls the real Google Document AI API. It self-skips unless `GOOGLE_APPLICATION_CREDENTIALS` and the processor env vars are present, so it never runs in CI.

### Test layers

| Layer | Count | What they cover |
|---|---|---|
| Domain | 23 | `Money`, `Invoice`, `Quarter` — pure business rules, no dependencies |
| Application | 24 | Use cases with NSubstitute port doubles — business logic in isolation |
| Integration | 64 | Real SQLite, ClosedXML, filesystem, zip; extraction mappers; WireMock stub for Resend |

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

## License

MIT © [Aitor Reviriego Amor](https://github.com/aitorevi)

See [LICENSE](LICENSE) for the full text.
