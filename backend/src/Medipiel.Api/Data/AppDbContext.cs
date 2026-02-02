using Medipiel.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Medipiel.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) {}

    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<ProductLine> Lines => Set<ProductLine>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Competitor> Competitors => Set<Competitor>();
    public DbSet<CompetitorProduct> CompetitorProducts => Set<CompetitorProduct>();
    public DbSet<PriceSnapshot> PriceSnapshots => Set<PriceSnapshot>();
    public DbSet<AlertRule> AlertRules => Set<AlertRule>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<SchedulerSettings> SchedulerSettings => Set<SchedulerSettings>();
    public DbSet<SchedulerRun> SchedulerRuns => Set<SchedulerRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Brand>().HasIndex(x => x.Name).IsUnique();
        modelBuilder.Entity<Brand>()
            .HasOne(x => x.Supplier)
            .WithMany()
            .HasForeignKey(x => x.SupplierId)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<Supplier>().HasIndex(x => x.Name).IsUnique();
        modelBuilder.Entity<Category>().HasIndex(x => x.Name).IsUnique();
        modelBuilder.Entity<ProductLine>().HasIndex(x => x.Name).IsUnique();
        modelBuilder.Entity<Competitor>().HasIndex(x => x.Name).IsUnique();

        modelBuilder.Entity<Product>()
            .HasIndex(x => x.Ean)
            .IsUnique()
            .HasFilter("[Ean] IS NOT NULL");
        modelBuilder.Entity<Product>()
            .HasOne(x => x.Line)
            .WithMany()
            .HasForeignKey(x => x.LineId)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<Product>()
            .Property(x => x.MedipielListPrice)
            .HasPrecision(18, 2);
        modelBuilder.Entity<Product>()
            .Property(x => x.MedipielPromoPrice)
            .HasPrecision(18, 2);

        modelBuilder.Entity<CompetitorProduct>()
            .HasIndex(x => new { x.ProductId, x.CompetitorId })
            .IsUnique();

        modelBuilder.Entity<PriceSnapshot>()
            .HasIndex(x => new { x.ProductId, x.CompetitorId, x.SnapshotDate })
            .IsUnique();

        modelBuilder.Entity<SchedulerSettings>().HasKey(x => x.Id);
        modelBuilder.Entity<SchedulerRun>().HasIndex(x => x.Status);
    }
}
