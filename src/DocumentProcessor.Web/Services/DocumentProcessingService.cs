using System.Threading.Channels;
using DocumentProcessor.Web.Models;
using DocumentProcessor.Web.Data;
using Microsoft.Extensions.Hosting;

namespace DocumentProcessor.Web.Services;

public class DocumentProcessingService(IServiceScopeFactory serviceScopeFactory, ILogger<DocumentProcessingService> logger) : BackgroundService
{
    private readonly Channel<Guid> _queue = Channel.CreateUnbounded<Guid>();
    private readonly SemaphoreSlim _semaphore = new(3, 3);

    public async Task<Guid> QueueDocumentForProcessingAsync(Guid documentId)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<DocumentRepository>();
        var doc = await repo.GetByIdAsync(documentId) ?? throw new ArgumentException($"Document {documentId} not found");
        doc.Status = DocumentStatus.Queued;
        doc.ProcessingStatus = "Queued";
        doc.ProcessingRetryCount = 0;
        await repo.UpdateAsync(doc);
        await _queue.Writer.WriteAsync(documentId);
        return documentId;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("Processing service starting with 3 workers");
        await Task.WhenAll(Enumerable.Range(0, 3).Select(i => ProcessTasksAsync(i, ct)));
    }

    private async Task ProcessTasksAsync(int id, CancellationToken ct)
    {
        await foreach (var docId in _queue.Reader.ReadAllAsync(ct))
        {
            await _semaphore.WaitAsync(ct);
            try { await ProcessDocumentAsync(docId); }
            catch (Exception ex) { logger.LogError(ex, "Worker {Id} error on {DocId}", id, docId); }
            finally { _semaphore.Release(); }
        }
    }

    private async Task ProcessDocumentAsync(Guid documentId)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<DocumentRepository>();
        var storage = scope.ServiceProvider.GetRequiredService<FileStorageService>();
        var ai = scope.ServiceProvider.GetRequiredService<AIService>();
        var doc = await repo.GetByIdAsync(documentId) ?? throw new ArgumentException($"Document {documentId} not found");

        try
        {
            doc.Status = DocumentStatus.Processing;
            doc.ProcessingStatus = "Processing";
            await repo.UpdateAsync(doc);

            await using var stream1 = await storage.GetDocumentAsync(doc.StoragePath);
            await using var stream2 = await storage.GetDocumentAsync(doc.StoragePath);
            var classificationTask = ai.ClassifyDocumentAsync(doc, stream1);
            var summaryTask = ai.SummarizeDocumentAsync(doc, stream2);
            await Task.WhenAll(classificationTask, summaryTask);
            var classification = await classificationTask;
            var summary = await summaryTask;

            doc.Status = DocumentStatus.Processed;
            doc.ProcessingStatus = "Completed";
            if (!string.IsNullOrEmpty(summary?.Summary)) doc.Summary = summary.Summary;
            if (!string.IsNullOrEmpty(classification?.PrimaryCategory))
            {
                doc.DocumentTypeName = classification.PrimaryCategory;
                doc.DocumentTypeCategory = classification.PrimaryCategory;
                if (string.IsNullOrEmpty(doc.ExtractedText))
                    doc.ExtractedText = $"Classification: {classification.PrimaryCategory}" + (classification.Tags.Any() ? $"; Tags: {string.Join(", ", classification.Tags)}" : "");
            }
            await repo.UpdateAsync(doc);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Process failed {DocumentId}", documentId);
            doc.Status = DocumentStatus.Failed;
            doc.ProcessingStatus = "Failed";
            doc.ProcessingErrorMessage = ex.Message;
            doc.ProcessingRetryCount++;
            await repo.UpdateAsync(doc);
        }
    }
}
