using System.Text;

namespace InvoiceProcessor.Integration.Tests.Fixtures;

public static class MinimalPdf
{
    // Minimal valid PDF — content is irrelevant because LlamaParse is stubbed in pipeline tests.
    private static readonly byte[] _bytes = Encoding.ASCII.GetBytes(
        "%PDF-1.4\n" +
        "1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n" +
        "2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n" +
        "3 0 obj<</Type/Page/MediaBox[0 0 3 3]>>endobj\n" +
        "xref\n0 4\n0000000000 65535 f\n0000000009 00000 n\n" +
        "0000000058 00000 n\n0000000115 00000 n\n" +
        "trailer<</Size 4/Root 1 0 R>>\nstartxref\n190\n%%EOF");

    public static byte[] Bytes() => _bytes;
}
