namespace DocumentProcessor.Web.Services;

public class FileStorageService(ILogger<FileStorageService> logger, IConfiguration configuration)
{
    private readonly string _basePath = InitPath(configuration, logger);

    private static string InitPath(IConfiguration cfg, ILogger<FileStorageService> log)
    {
        var path = cfg["DocumentProcessing:StoragePath"] ?? "uploads";
        if (!Directory.Exists(path)) { Directory.CreateDirectory(path); log.LogDebug("Created {Path}", path); }
        return path;
    }

    public Task<Stream> GetDocumentAsync(string path)
    {
        var fullPath = GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException($"Not found: {path}");
        return Task.FromResult<Stream>(new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
    }

    public async Task<string> SaveDocumentAsync(Stream stream, string fileName)
    {
        var uniqueName = $"{Path.GetFileNameWithoutExtension(fileName)}_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}"[..50] + Path.GetExtension(fileName);
        var relativePath = Path.Combine(DateTime.UtcNow.ToString("yyyy/MM/dd"), uniqueName);
        var fullPath = GetFullPath(relativePath);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        using (var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
            await stream.CopyToAsync(fs);
        return relativePath;
    }

    public async Task<bool> DeleteDocumentAsync(string path)
    {
        var fullPath = GetFullPath(path);
        if (!File.Exists(fullPath)) return false;
        for (int i = 0; i < 3; i++)
        {
            try { File.Delete(fullPath); return true; }
            catch (IOException) { if (i < 2) await Task.Delay(500 * (i + 1)); else return false; }
        }
        return false;
    }

    private string GetFullPath(string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_basePath, relativePath));
        if (!fullPath.StartsWith(Path.GetFullPath(_basePath))) throw new UnauthorizedAccessException("Access denied");
        return fullPath;
    }
}
