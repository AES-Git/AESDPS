using Microsoft.EntityFrameworkCore;
using DocumentProcessor.Web.Models;

namespace DocumentProcessor.Web.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Document> Documents { get; set; }

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Document>(e =>
        {
            e.HasKey(d => d.Id);
            e.ToTable("Documents");
            e.Property(d => d.FileName).IsRequired().HasMaxLength(500);
            e.Property(d => d.OriginalFileName).IsRequired().HasMaxLength(500);
            e.Property(d => d.FileExtension).HasMaxLength(50);
            e.Property(d => d.ContentType).HasMaxLength(100);
            e.Property(d => d.StoragePath).HasMaxLength(1000);
            e.Property(d => d.UploadedBy).IsRequired().HasMaxLength(255);
            e.Property(d => d.DocumentTypeName).HasMaxLength(255);
            e.Property(d => d.DocumentTypeCategory).HasMaxLength(100);
            e.Property(d => d.ProcessingStatus).HasMaxLength(50);
            e.Property(d => d.ProcessingErrorMessage).HasMaxLength(1000);
            e.HasIndex(d => d.Status);
            e.HasIndex(d => d.UploadedAt);
            e.HasIndex(d => d.IsDeleted);
            e.HasQueryFilter(d => !d.IsDeleted);
        });
    }

    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (var e in ChangeTracker.Entries<Document>().Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            if (e.State == EntityState.Added) e.Entity.CreatedAt = DateTime.UtcNow;
            e.Entity.UpdatedAt = DateTime.UtcNow;
        }
        return await base.SaveChangesAsync(ct);
    }
}
