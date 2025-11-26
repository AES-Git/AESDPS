using System.Threading.Channels;
using DocumentProcessor.Web.Models;
using DocumentProcessor.Web.Data;
using Microsoft.Extensions.Hosting;

namespace DocumentProcessor.Web.Services;

public class DocumentProcessingService(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<DocumentProcessingService> logger) : BackgroundService
{
    private readonly Channel<Guid> _queue = Channel.CreateUnbounded<Guid>();
    private readonly SemaphoreSlim _semaphore = new(MaxConcurrency, MaxConcurrency);
    private const int MaxConcurrency = 3;

    public async Task<Guid> QueueDocumentForProcessingAsync(Guid documentId)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var documentRepository = scope.ServiceProvider.GetRequiredService<DocumentRepository>();

        var document = await documentRepository.GetByIdAsync(documentId);
        if (document == null)
        {
            throw new ArgumentException($"Document with ID {documentId} not found");
        }

        document.Status = DocumentStatus.Queued;
        document.ProcessingStatus = "Queued";
        document.ProcessingRetryCount = 0;
        document.UpdatedAt = DateTime.UtcNow;
        await documentRepository.UpdateAsync(document);

        await _queue.Writer.WriteAsync(documentId);
        logger.LogInformation("Document {DocumentId} queued for processing", documentId);

        return documentId;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Document Processing Service is starting with max concurrency: {MaxConcurrency}", MaxConcurrency);

        List<Task> tasks = [];
        for (int i = 0; i < MaxConcurrency; i++)
        {
            tasks.Add(ProcessTasksAsync(i, stoppingToken));
        }

        await Task.WhenAll(tasks);
        logger.LogInformation("Document Processing Service is stopping");
    }

    private async Task ProcessTasksAsync(int workerId, CancellationToken stoppingToken)
    {
        logger.LogInformation("Worker {WorkerId} started", workerId);

        await foreach (var documentId in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            await _semaphore.WaitAsync(stoppingToken);

            try
            {
                logger.LogInformation("Worker {WorkerId} processing document {DocumentId}", workerId, documentId);
                await ProcessDocumentAsync(documentId);
                logger.LogInformation("Worker {WorkerId} completed document {DocumentId}", workerId, documentId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Worker {WorkerId} encountered error processing document {DocumentId}", workerId, documentId);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        logger.LogInformation("Worker {WorkerId} stopped", workerId);
    }

    private async Task ProcessDocumentAsync(Guid documentId)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var documentRepository = scope.ServiceProvider.GetRequiredService<DocumentRepository>();
        var fileStorage = scope.ServiceProvider.GetRequiredService<FileStorageService>();
        var aiService = scope.ServiceProvider.GetRequiredService<AIService>();

        var document = await documentRepository.GetByIdAsync(documentId);
        if (document == null)
        {
            throw new ArgumentException($"Document with ID {documentId} not found");
        }

        try
        {
            document.Status = DocumentStatus.Processing;
            document.ProcessingStatus = "Processing";
            document.ProcessingStartedAt = DateTime.UtcNow;
            document.UpdatedAt = DateTime.UtcNow;
            await documentRepository.UpdateAsync(document);

            logger.LogInformation("Processing document {DocumentId}", documentId);

            await using var classificationStream = await fileStorage.GetDocumentAsync(document.StoragePath);
            await using var summaryStream = await fileStorage.GetDocumentAsync(document.StoragePath);

            var classificationTask = aiService.ClassifyDocumentAsync(document, classificationStream);
            var summaryTask = aiService.SummarizeDocumentAsync(document, summaryStream);

            await Task.WhenAll(classificationTask, summaryTask);

            var classification = await classificationTask;
            var summary = await summaryTask;

            document.Status = DocumentStatus.Processed;
            document.ProcessedAt = DateTime.UtcNow;
            document.ProcessingStatus = "Completed";
            document.ProcessingCompletedAt = DateTime.UtcNow;
            document.UpdatedAt = DateTime.UtcNow;

            if (summary != null && !string.IsNullOrEmpty(summary.Summary))
            {
                document.Summary = summary.Summary;
            }

            if (classification != null && !string.IsNullOrEmpty(classification.PrimaryCategory))
            {
                document.DocumentTypeName = classification.PrimaryCategory;
                document.DocumentTypeCategory = classification.PrimaryCategory;

                if (string.IsNullOrEmpty(document.ExtractedText))
                {
                    document.ExtractedText = $"Classification: {classification.PrimaryCategory}";
                    if (classification.Tags.Count != 0)
                    {
                        document.ExtractedText += $"; Tags: {string.Join(", ", classification.Tags)}";
                    }
                }
            }

            await documentRepository.UpdateAsync(document);

            logger.LogInformation("Successfully processed document {DocumentId}", documentId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing document {DocumentId}", documentId);

            document.Status = DocumentStatus.Failed;
            document.ProcessingStatus = "Failed";
            document.ProcessingErrorMessage = ex.Message;
            document.ProcessingCompletedAt = DateTime.UtcNow;
            document.ProcessingRetryCount++;
            document.UpdatedAt = DateTime.UtcNow;
            await documentRepository.UpdateAsync(document);
        }
    }
}
