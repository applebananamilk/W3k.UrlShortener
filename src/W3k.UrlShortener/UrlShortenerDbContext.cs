using Microsoft.EntityFrameworkCore;

namespace W3k.UrlShortener;

public class UrlShortenerDbContext(DbContextOptions<UrlShortenerDbContext> options) : DbContext(options)
{
    public DbSet<UrlMapping> UrlMappings { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<UrlMapping>(b =>
        {
            b.HasKey(p => p.Key);
            b.Property(p => p.OriginalUrl).IsRequired();
        });
    }
}
