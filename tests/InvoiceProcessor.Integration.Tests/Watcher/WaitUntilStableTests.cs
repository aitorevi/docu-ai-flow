using InvoiceProcessor.Infrastructure.Files;

namespace InvoiceProcessor.Integration.Tests.Watcher;

public sealed class WaitUntilStableTests : IDisposable
{
    private readonly string _dir;

    public WaitUntilStableTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"stability-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task WaitsUntilFileSizeStabilizes()
    {
        // Given — a file path that will be written in parts with delays
        var path = Path.Combine(_dir, "growing.pdf");

        // Write initial content and start a background task that appends more bytes after a delay
        await File.WriteAllBytesAsync(path, new byte[100]);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var appendTask = Task.Run(async () =>
        {
            await Task.Delay(300, cts.Token);
            await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            await stream.WriteAsync(new byte[200], cts.Token);
            await stream.FlushAsync(cts.Token);
        }, cts.Token);

        // When — we wait until stable
        await FileStabilityWaiter.WaitUntilStableAsync(path, cts.Token);
        await appendTask;

        // Then — the file has the final size (not the intermediate size)
        var info = new FileInfo(path);
        Assert.Equal(300, info.Length);
    }

    [Fact]
    public async Task ReturnsEarlyIfFileAlreadyStable()
    {
        // Given — a file that is already complete before we call WaitUntilStableAsync
        var path = Path.Combine(_dir, "stable.pdf");
        await File.WriteAllBytesAsync(path, new byte[50]);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // When — we wait, recording elapsed time
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await FileStabilityWaiter.WaitUntilStableAsync(path, cts.Token);
        sw.Stop();

        // Then — returns well before the maximum wait of 5 seconds
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3),
            $"Expected early return but waited {sw.Elapsed.TotalSeconds:F1}s");
    }
}
