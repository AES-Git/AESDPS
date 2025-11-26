using Microsoft.EntityFrameworkCore;
using DocumentProcessor.Web.Models;

namespace DocumentProcessor.Web.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{

    public DbSet<Document> Documents { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Document>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.ToTable("Documents");

            entity.Property(e => e.FileName).IsRequired().HasMaxLength(500);
            entity.Property(e => e.OriginalFileName).IsRequired().HasMaxLength(500);
            entity.Property(e => e.FileExtension).HasMaxLength(50);
            entity.Property(e => e.ContentType).HasMaxLength(100);
            entity.Property(e => e.StoragePath).HasMaxLength(1000);
            entity.Property(e => e.S3Key).HasMaxLength(500);
            entity.Property(e => e.S3Bucket).HasMaxLength(255);
            entity.Property(e => e.UploadedBy).IsRequired().HasMaxLength(255);
            entity.Property(e => e.DocumentTypeName).HasMaxLength(255);
            entity.Property(e => e.DocumentTypeCategory).HasMaxLength(100);
            entity.Property(e => e.ProcessingStatus).HasMaxLength(50);
            entity.Property(e => e.ProcessingErrorMessage).HasMaxLength(1000);

            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.UploadedAt);
            entity.HasIndex(e => e.IsDeleted);
            entity.HasIndex(e => e.ProcessingStatus);

            entity.HasQueryFilter(e => !e.IsDeleted);
        });
    }

    public override int SaveChanges()
    {
        UpdateTimestamps();
        return base.SaveChanges();
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateTimestamps();
        return await base.SaveChangesAsync(cancellationToken);
    }

    private void UpdateTimestamps()
    {
        var entries = ChangeTracker.Entries<Document>()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified);

        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = DateTime.UtcNow;
            }
            entry.Entity.UpdatedAt = DateTime.UtcNow;
        }
    }
}
