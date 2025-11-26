using Microsoft.Extensions.Configuration;

namespace DocumentProcessor.Web.Services;

public class FileStorageService(ILogger<FileStorageService> logger, IConfiguration configuration)
{
    private readonly string _basePath = InitializeBasePath(configuration, logger);

    private static string InitializeBasePath(IConfiguration configuration, ILogger<FileStorageService> logger)
    {
        var basePath = configuration["DocumentProcessing:StoragePath"] ?? "uploads";
        EnsureDirectoryExists(basePath, logger);
        return basePath;
    }

    public async Task<Stream> GetDocumentAsync(string path)
    {
        try
        {
            var fullPath = GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"Document not found at path: {path}");
            }

            return await Task.FromResult(new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting document stream for path: {Path}", path);
            throw;
        }
    }

    public async Task<string> SaveDocumentAsync(Stream documentStream, string fileName)
    {
        try
        {
            var uniqueFileName = GenerateUniqueFileName(fileName);
            var relativePath = Path.Combine(DateTime.UtcNow.ToString("yyyy/MM/dd"), uniqueFileName);
            var fullPath = GetFullPath(relativePath);

            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                EnsureDirectoryExists(directory, logger);
            }

            using (var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await documentStream.CopyToAsync(fileStream);
            }

            logger.LogInformation("Document saved successfully at: {Path}", relativePath);
            return relativePath;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error saving document: {FileName}", fileName);
            throw;
        }
    }

    public async Task<bool> DeleteDocumentAsync(string path)
    {
        try
        {
            var fullPath = GetFullPath(path);
            if (File.Exists(fullPath))
            {
                int maxRetries = 3;
                int delayMs = 500;

                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        File.Delete(fullPath);
                        logger.LogInformation("Document deleted: {Path}", path);
                        return true;
                    }
                    catch (IOException ex) when (ex.Message.Contains("being used by another process"))
                    {
                        if (attempt < maxRetries)
                        {
                            logger.LogWarning("File is in use, waiting before retry {Attempt}/{MaxRetries}: {Path}",
                                attempt, maxRetries, path);
                            await Task.Delay(delayMs * attempt);
                        }
                        else
                        {
                            logger.LogWarning("File is still in use after {MaxRetries} attempts, cannot delete: {Path}", maxRetries, path);
                            return false;
                        }
                    }
                }

                return false;
            }

            logger.LogWarning("Document not found for deletion: {Path}", path);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting document: {Path}", path);
            throw;
        }
    }

    private string GetFullPath(string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_basePath, relativePath));
        var baseFullPath = Path.GetFullPath(_basePath);

        if (!fullPath.StartsWith(baseFullPath))
        {
            throw new UnauthorizedAccessException("Access to path outside of base directory is not allowed");
        }

        return fullPath;
    }

    private static void EnsureDirectoryExists(string path, ILogger<FileStorageService> logger)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
            logger.LogDebug("Created directory: {Path}", path);
        }
    }

    private string GenerateUniqueFileName(string fileName)
    {
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var guid = Guid.NewGuid().ToString("N").Substring(0, 8);

        return $"{fileNameWithoutExtension}_{timestamp}_{guid}{extension}";
    }
}
