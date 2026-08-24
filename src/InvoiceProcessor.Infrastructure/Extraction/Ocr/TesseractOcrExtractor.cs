using System.Diagnostics;
using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Infrastructure.Extraction.Templates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvoiceProcessor.Infrastructure.Extraction.Ocr;

// OCR fallback that shells out to poppler (pdftoppm) to rasterise the first page and
// tesseract to read it. Best-effort: any failure (tools missing, timeout, bad PDF) is
// logged and turned into an empty string so the caller degrades to manual entry.
public sealed class TesseractOcrExtractor(
    IOptions<TemplateExtractorOptions> opts,
    ILogger<TesseractOcrExtractor> logger) : IOcrTextExtractor
{
    private readonly OcrFallbackOptions _ocr = opts.Value.OcrFallback;

    public async Task<string> ExtractTextAsync(ReadOnlyMemory<byte> pdfBytes, CancellationToken ct)
    {
        var baseTmp = string.IsNullOrWhiteSpace(_ocr.TempDir) ? Path.GetTempPath() : _ocr.TempDir!;
        var tempDir = Path.Combine(baseTmp, "ocr-" + Guid.NewGuid().ToString("N"));

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_ocr.TimeoutSeconds));
        var token = timeoutCts.Token;

        try
        {
            Directory.CreateDirectory(tempDir);
            var pdfPath = Path.Combine(tempDir, "input.pdf");
            await File.WriteAllBytesAsync(pdfPath, pdfBytes.ToArray(), token);

            // 1) Rasterise the first page to PNG at 300 dpi.
            var pngPrefix = Path.Combine(tempDir, "page");
            var rasterOk = await RunAsync("pdftoppm",
                ["-png", "-r", "300", "-f", "1", "-l", "1", pdfPath, pngPrefix], token);
            if (!rasterOk) return string.Empty;

            // poppler names the file page-1.png / page-01.png depending on version; do not assume.
            var png = Directory.EnumerateFiles(tempDir, "page*.png").FirstOrDefault();
            if (png is null)
            {
                logger.LogWarning("OCR: pdftoppm produced no PNG for a {Bytes}-byte PDF.", pdfBytes.Length);
                return string.Empty;
            }

            // 2) OCR the image to stdout.
            var (ok, stdout) = await RunCaptureAsync("tesseract", [png, "stdout", "-l", _ocr.Language], token);
            return ok ? stdout.Trim() : string.Empty;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OCR failed for a {Bytes}-byte PDF; degrading to manual entry.", pdfBytes.Length);
            return string.Empty;
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }

    private async Task<bool> RunAsync(string file, string[] args, CancellationToken ct)
    {
        var (ok, _) = await RunCaptureAsync(file, args, ct);
        return ok;
    }

    private async Task<(bool ok, string stdout)> RunCaptureAsync(string file, string[] args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = new Process { StartInfo = psi };
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var stdout = await stdoutTask;

        if (process.ExitCode != 0)
        {
            var stderr = await stderrTask;
            logger.LogWarning("OCR: {File} exited {Code}. {Stderr}", file, process.ExitCode, stderr.Trim());
            return (false, string.Empty);
        }
        return (true, stdout);
    }
}
