namespace InvoiceProcessor.Infrastructure.Files;

public static class FileStabilityWaiter
{
    public static async Task WaitUntilStableAsync(string path, CancellationToken ct)
    {
        long last = -1;
        for (var i = 0; i < 10; i++)
        {
            try
            {
                var len = new FileInfo(path).Length;
                if (len == last && len > 0) return;
                last = len;
            }
            catch (IOException) { /* file still locked */ }
            await Task.Delay(500, ct);
        }
    }
}
