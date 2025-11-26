using Microsoft.EntityFrameworkCore;
using DocumentProcessor.Web.Models;
using System.Linq.Expressions;

namespace DocumentProcessor.Web.Data;

public class DocumentRepository(AppDbContext context)
{
    public async Task<Document?> GetByIdAsync(Guid id)
    {
        return await context.Documents.FirstOrDefaultAsync(d => d.Id == id);
    }

    public async Task<IEnumerable<Document>> GetAllAsync()
    {
        return await context.Documents
            .Where(d => !d.IsDeleted)
            .OrderByDescending(d => d.UploadedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<Document>> GetByStatusAsync(DocumentStatus status)
    {
        return await context.Documents
            .Where(d => d.Status == status)
            .OrderByDescending(d => d.UploadedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<Document>> GetPendingDocumentsAsync(int limit = 100)
    {
        return await context.Documents
            .Where(d => d.Status == DocumentStatus.Pending || d.Status == DocumentStatus.Queued)
            .OrderBy(d => d.UploadedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<Document> AddAsync(Document document)
    {
        if (document.Id == Guid.Empty)
            document.Id = Guid.NewGuid();

        document.CreatedAt = DateTime.UtcNow;
        document.UpdatedAt = DateTime.UtcNow;

        await context.Documents.AddAsync(document);
        await context.SaveChangesAsync();
        return document;
    }

    public async Task<Document> UpdateAsync(Document document)
    {
        document.UpdatedAt = DateTime.UtcNow;
        context.Documents.Update(document);
        await context.SaveChangesAsync();
        return document;
    }

    public async Task DeleteAsync(Guid id)
    {
        var document = await context.Documents.FindAsync(id);
        if (document != null)
        {
            context.Documents.Remove(document);
            await context.SaveChangesAsync();
        }
    }

    public async Task SoftDeleteAsync(Guid id)
    {
        var document = await context.Documents.FindAsync(id);
        if (document != null)
        {
            document.IsDeleted = true;
            document.DeletedAt = DateTime.UtcNow;
            document.UpdatedAt = DateTime.UtcNow;
            context.Documents.Update(document);
            await context.SaveChangesAsync();
        }
    }

    public async Task<bool> ExistsAsync(Guid id)
    {
        return await context.Documents.AnyAsync(d => d.Id == id);
    }

    public async Task<int> CountAsync()
    {
        return await context.Documents.CountAsync();
    }

    public async Task<int> CountAsync(Expression<Func<Document, bool>> predicate)
    {
        return await context.Documents.CountAsync(predicate);
    }

    public async Task<IEnumerable<Document>> GetPagedAsync(int pageNumber, int pageSize)
    {
        return await context.Documents
            .Where(d => !d.IsDeleted)
            .OrderByDescending(d => d.UploadedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }
}
