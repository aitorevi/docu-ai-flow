# Configuration

Nothing here is required to run the project: with no `.env` at all it uses the local template
extractor, offline and for free. Each section below buys an optional capability.

## What each capability needs

Only the first one is mandatory. Everything else buys an optional capability.

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- **Only** for `Extraction:Provider=DocumentAi`: a [Google Cloud](https://cloud.google.com/document-ai) project with a Document AI **Invoice Parser** processor and a service-account JSON key. The default local extractor needs neither.
- **Only** for the OCR fallback on scanned invoices: `poppler` and `tesseract`.
  macOS `brew install poppler tesseract tesseract-lang` · Debian/Ubuntu `apt-get install poppler-utils tesseract-ocr tesseract-ocr-spa`
- **Only** for email dispatch: a [Resend](https://resend.com) account and a verified domain
- **Only** to change the UI: [Node.js](https://nodejs.org). The built site is committed, so running the app never needs it.
- [Claude Code](https://claude.ai/code) (optional, for AI-assisted development)
- [gh](https://cli.github.com) (optional, for PR management)

## Where settings live

The recommended way to configure the app is via a `.env` file in the repo root (gitignored). Copy `.env.example` to get started:

```
# Extraction backend: Template (default, local, free) or DocumentAi (Google, paid).
# Leave it unset and you get Template.
Extraction__Provider=Template

# Only for Extraction__Provider=DocumentAi. Credentials are loaded from the
# service-account JSON pointed to by GOOGLE_APPLICATION_CREDENTIALS (ADC).
GOOGLE_APPLICATION_CREDENTIALS=/path/to/service-account.json
GoogleDocumentAi__ProjectId=your-gcp-project-id
GoogleDocumentAi__Location=eu
GoogleDocumentAi__ProcessorId=your-processor-id

# Only for scanned invoices; requires poppler + tesseract on the machine.
TemplateExtractor__OcrFallback__Enabled=false

# Your own company identity — used to filter the buyer out of the extracted supplier fields.
Company__TaxId=
Company__Name=

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
    "Provider": "Template",
    "ConfidenceThreshold": 0.6,
    "SupplierTrustThreshold": 3
  },
  "TemplateExtractor": {
    "MinTextLength": 50,
    "OcrFallback": { "Enabled": false, "Language": "spa", "TimeoutSeconds": 60 },
    "Templates": [
      {
        "SupplierId": "aurora",
        "SupplierName": "Suministros Aurora S.L.",
        "SupplierTaxId": "B12345674",
        "IdentificationAnchors": ["SUMINISTROS AURORA", "B12345674"],
        "Fields": {
          "invoice_number": { "Anchors": ["Factura Nº"], "Pattern": ":\\s*(\\S+)" },
          "issue_date":     { "Anchors": ["Fecha"], "Pattern": ":\\s*([\\d/]+)" },
          "net_amount":     { "Anchors": ["Base imponible"], "Pattern": ":\\s*([\\d.,]+)" }
        }
      }
    ]
  },
  "SupplierCatalog": {
    "Suppliers": [
      { "CanonicalName": "Suministros Aurora", "TaxId": "B12345674", "Aliases": ["SUMINISTROS AURORA S.L."] }
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
| `Extraction:Provider` | `Template` (default, local, free) or `DocumentAi` (Google, paid). An unrecognised value falls back to `Template` |
| `TemplateExtractor:Templates` | Per-supplier anchors and regexes. `IdentificationAnchors` pick the supplier; each field has its own `Anchors` + `Pattern` whose first capture group is the value |
| `TemplateExtractor:MinTextLength` | Below this many characters the PDF is treated as a scan |
| `TemplateExtractor:OcrFallback:Enabled` | Rasterise scans with poppler and read them with Tesseract. Off by default |
| `GoogleDocumentAi:Location` | Document AI region; must match the processor's location (e.g. `eu`, `us`) |
| `GoogleDocumentAi:ProcessorId` | The Invoice Parser processor id from the Google Cloud console |
| `Company:TaxId` / `Company:Name` | Your own identity — filtered out of the extracted supplier fields |
| `Resend:FromAddress` | Must belong to a domain verified in Resend (DKIM/SPF) |
| `Resend:CcAddress` | Optional. When set, a copy of every advisor email is sent here |
| `Extraction:ConfidenceThreshold` | Extractions with lower average confidence are moved to `failed/` |
| `Extraction:SupplierTrustThreshold` | Consecutive corrections-free confirmations before a supplier's invoices are filed without review. Ships as `3` so the demo is watchable; use `20` for real use |
| `MailDispatch:MaxAttachmentMb` | ZIP size limit before splitting by month (default 38 MB — Resend's hard limit is 40 MB) |
