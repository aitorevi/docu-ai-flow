namespace InvoiceProcessor.Application.Dispatch;

public sealed record CompressedArchive(string FileName, string Path, long FileSizeBytes = 0);
