# docu-ai-flow

Folder-watching .NET 10 service that extracts invoice data from PDFs, exports to Excel, and emails quarterly archives to your advisor.

Drop a PDF into `./data/inbox/`. The service detects it, extracts the invoice fields, and either files it or **holds it for review** depending on how much that supplier has earned the benefit of the doubt. Confirmed invoices are persisted in SQLite, archived, and rolled into a master Excel spreadsheet. When a quarter closes, one command ZIPs the PDFs and emails them to your tax advisor via [Resend](https://resend.com).

Built with **.NET 10**, **C#**, and a strict **hexagonal architecture**. Business errors use the **Result pattern** (`SharpMonads.Core`); infrastructure failures bubble up to Polly.

**It runs with no accounts and no API keys.** One command, no clone required:

```bash
docker run --rm -v "$PWD/data:/app/data" ghcr.io/aitorevi/docu-ai-flow:latest make-samples /app/data/inbox
docker run --rm -p 8080:8080 -v "$PWD/data:/app/data" ghcr.io/aitorevi/docu-ai-flow:latest
```

Then open **http://localhost:8080**. The first command writes eight fictional invoices into the inbox; the second starts the watcher, extracts what it can, and parks the rest at **`/review`** with the PDF beside the form.

![The panel: four headline numbers and a trust meter per supplier](docs/img/panel.png)

Three of those invoices come from the same supplier on purpose. Confirm all three without changing anything and you watch that supplier cross from **En revisión** to **Automático** — after which its invoices are filed without anyone looking at them. That is the whole idea of the project, and it takes about a minute to see.

---

## The two ideas

**Extraction is a swappable detail.** It sits behind one port with two live adapters — local
templates over [PdfPig](https://github.com/UglyToad/PdfPig) (the default: no network, no
credentials, 0 € per invoice) and Google Document AI. Which one is bound is
`Extraction:Provider`, not a code change.

The project started on LlamaParse, moved to Document AI when its accuracy fell short, and
ended up on the local extractor that beat both on the invoices it actually sees. Across all
three swaps **the domain, the use cases and the pipeline tests never changed** — they depend
on the port, not the provider. → **[Extraction](docs/extraction.md)**

**No invoice is filed unseen until its supplier earns it.** Extraction is never 100% right, so
the interesting question is not whether a human reviews but *which invoices earn the right to
skip review* — decided per supplier, by track record. N consecutive confirmations with no
correction and that supplier is filed automatically; one correction resets the counter and
revokes it. → **[How it works](docs/how-it-works.md)**

![The review screen: the PDF beside the extracted fields, each with its confidence](docs/img/review.png)

### Try it with no configuration at all

```bash
cp data/samples/*.pdf data/inbox/          # running directly
docker compose run --rm app make-samples /app/data/inbox   # under Docker
```

Eight fictional invoices, chosen to walk every interesting path:

| File | What it exercises |
|---|---|
| `aurora-A-2026-*.pdf` (×3) | `label: value` layout, Spanish numbers and dates. **Three from one supplier**, so confirming them all walks it to *Automático* |
| `boreal-FR-2026-*.pdf` (×2) | column table: the values sit on the line after the header |
| `cronos-C-26-0311.pdf` | dot leaders, US numbers, English month |
| `desconocido-TD-2026-77.pdf` | no template for this supplier → manual entry, not a guess |
| `escaneo-sin-texto.pdf` | no text layer → OCR fallback, or manual entry when OCR is off |

Regenerate them any time with `dotnet run --project src/InvoiceProcessor.Worker -- make-samples`. `ShippedDemoTemplatesTests` runs every one of them through the shipped templates, so a broken sample fails the build rather than the demo.

## Running it

Needs a Docker engine with `docker compose`: OrbStack or Docker Desktop on macOS, Docker Desktop or Docker Engine under WSL2 on Windows.

```bash
git clone https://github.com/aitorevi/docu-ai-flow
cd docu-ai-flow
docker compose run --rm app make-samples /app/data/inbox   # optional: the demo invoices
docker compose up -d
open http://localhost:8080
```

That is the whole setup. No `.env`, no accounts, no .NET or Node on the machine.

| | |
|---|---|
| Port | `8080`. Not 5000 — on macOS that port belongs to the AirPlay receiver |
| Data | `./data` on the host is `/app/data` in the container: same layout on both sides, and the SQLite database survives the container |
| Elsewhere | Point `DATA_DIR` at another folder in `.env` |
| Update | `docker compose pull && docker compose up -d` |
| Logs / stop | `docker compose logs -f` · `docker compose down` |
| Config | Add a `.env` only to switch to Document AI, enable OCR, or send email — see [Configuration](docs/configuration.md). Without one the app uses the local extractor |

The image ships poppler and Tesseract, so enabling the OCR fallback is a config flag rather than a rebuild.

Prefer to run it without Docker, or want to work on the code? → **[Development](docs/development.md)**

## Stack

| Layer | Technology |
|---|---|
| Runtime | .NET 10 (ASP.NET Core + Worker Service) |
| Architecture | Hexagonal (ports & adapters) |
| Error handling | `Result<TValue, TError>` via SharpMonads.Core |
| Persistence | SQLite (`Microsoft.Data.Sqlite`) |
| Excel output | ClosedXML |
| Extraction | Local templates over PdfPig (default); Google Document AI (Invoice Parser) |
| OCR fallback | poppler (`pdftoppm`) + Tesseract, optional |
| Email | Resend REST API |
| Resilience | Polly (`AddStandardResilienceHandler`) |
| Tests | xUnit + NSubstitute + WireMock.Net + NetArchTest |

## Documentation

| | |
|---|---|
| **[Extraction](docs/extraction.md)** | The port and its adapters, the OCR fallback, and how to write a template |
| **[How it works](docs/how-it-works.md)** | The pipeline end to end, and the per-supplier trust model |
| **[Configuration](docs/configuration.md)** | `.env`, `appsettings.json`, and what each capability needs |
| **[Development](docs/development.md)** | Running without Docker, project layout, tests, architecture |

## License

MIT © [Aitor Reviriego Amor](https://github.com/aitorevi)

See [LICENSE](LICENSE) for the full text.
