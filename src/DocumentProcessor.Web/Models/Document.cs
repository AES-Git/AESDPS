namespace DocumentProcessor.Web.Models;

public enum DocumentStatus { Pending, Queued, Processing, Processed, Failed }
public enum DocumentSource { LocalUpload, S3, FileShare, Email }

public class Document
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string FileExtension { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
    public DocumentSource Source { get; set; }
    public DocumentStatus Status { get; set; }
    public string? DocumentTypeName { get; set; }
    public string? DocumentTypeCategory { get; set; }
    public string? ProcessingStatus { get; set; }
    public int ProcessingRetryCount { get; set; }
    public string? ProcessingErrorMessage { get; set; }
    public DateTime? ProcessingStartedAt { get; set; }
    public DateTime? ProcessingCompletedAt { get; set; }
    public string? ExtractedText { get; set; }
    public string? Summary { get; set; }
    public DateTime UploadedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public string UploadedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
