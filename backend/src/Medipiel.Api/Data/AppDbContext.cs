using Medipiel.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Medipiel.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) {}

    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Competitor> Competitors => Set<Competitor>();
    public DbSet<CompetitorProduct> CompetitorProducts => Set<CompetitorProduct>();
    public DbSet<PriceSnapshot> PriceSnapshots => Set<PriceSnapshot>();
    public DbSet<AlertRule> AlertRules => Set<AlertRule>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Brand>().HasIndex(x => x.Name).IsUnique();
        modelBuilder.Entity<Supplier>().HasIndex(x => x.Name).IsUnique();
        modelBuilder.Entity<Category>().HasIndex(x => x.Name).IsUnique();
        modelBuilder.Entity<Competitor>().HasIndex(x => x.Name).IsUnique();

        modelBuilder.Entity<Product>().HasIndex(x => x.Sku).IsUnique();
        modelBuilder.Entity<Product>().HasIndex(x => x.Ean);

        modelBuilder.Entity<CompetitorProduct>()
            .HasIndex(x => new { x.ProductId, x.CompetitorId })
            .IsUnique();

        modelBuilder.Entity<PriceSnapshot>()
            .HasIndex(x => new { x.ProductId, x.CompetitorId, x.SnapshotDate })
            .IsUnique();
    }
}
