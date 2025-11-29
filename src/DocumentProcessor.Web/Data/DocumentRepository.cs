using Microsoft.EntityFrameworkCore;
using DocumentProcessor.Web.Models;

namespace DocumentProcessor.Web.Data;

public class DocumentRepository(AppDbContext context)
{
    public Task<Document?> GetByIdAsync(Guid id) => context.Documents.FirstOrDefaultAsync(d => d.Id == id);
    public Task<List<Document>> GetAllAsync() => context.Documents.Where(d => !d.IsDeleted).ToListAsync();
    public Task<List<Document>> GetByStatusAsync(DocumentStatus status) => context.Documents.Where(d => d.Status == status).ToListAsync();

    public async Task<Document> AddAsync(Document doc)
    {
        if (doc.Id == Guid.Empty) doc.Id = Guid.NewGuid();
        await context.Documents.AddAsync(doc);
        await context.SaveChangesAsync();
        return doc;
    }

    public async Task<Document> UpdateAsync(Document doc)
    {
        context.Documents.Update(doc);
        await context.SaveChangesAsync();
        return doc;
    }

    public async Task DeleteAsync(Guid id)
    {
        var doc = await context.Documents.FindAsync(id);
        if (doc != null) { context.Documents.Remove(doc); await context.SaveChangesAsync(); }
    }
}
