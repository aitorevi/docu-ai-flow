using InvoiceProcessor.Infrastructure.Dispatch;
using System.IO.Compression;

namespace InvoiceProcessor.Integration.Tests.Dispatch;

public sealed class ZipInvoiceArchiveCompressorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public ZipInvoiceArchiveCompressorTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task CompressAsync_creates_zip_with_all_files()
    {
        // Given
        var file1 = Path.Combine(_tempDir, "invoice1.pdf");
        var file2 = Path.Combine(_tempDir, "invoice2.pdf");
        await File.WriteAllBytesAsync(file1, [0x25, 0x50, 0x44, 0x46]); // %PDF header
        await File.WriteAllBytesAsync(file2, [0x25, 0x50, 0x44, 0x46]);

        var sut = new ZipInvoiceArchiveCompressor();

        // When
        var archive = await sut.CompressAsync([file1, file2], "test-archive", CancellationToken.None);

        // Then
        Assert.True(File.Exists(archive.Path));
        Assert.Equal("test-archive.zip", archive.FileName);

        using var zip = ZipFile.OpenRead(archive.Path);
        var entryNames = zip.Entries.Select(e => e.Name).ToHashSet();
        Assert.Contains("invoice1.pdf", entryNames);
        Assert.Contains("invoice2.pdf", entryNames);
        Assert.Equal(2, zip.Entries.Count);

        // Cleanup
        File.Delete(archive.Path);
    }
}
