using Microsoft.EntityFrameworkCore;
using StealDeal.Services.Store.Domain.Models;

namespace StealDeal.Services.Store.Infrastructure.Persistence
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public DbSet<Category> Categories { get; set; }
        public DbSet<StoreProfile> StoreProfiles { get; set; }
        public DbSet<SurpriseBag> SurpriseBags { get; set; }
        public DbSet<StoreReview> StoreReviews { get; set; }
        public DbSet<OutboxMessage> OutboxMessages { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Category Configuration
            modelBuilder.Entity<Category>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Slug).IsRequired().HasMaxLength(100);
                entity.HasIndex(e => e.Slug).IsUnique();
                entity.Property(e => e.IconUrl).HasMaxLength(1000);
            });

            // StoreProfile Configuration
            modelBuilder.Entity<StoreProfile>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.OwnerId).IsRequired();
                entity.HasIndex(e => e.OwnerId).IsUnique(); // 1 owner = 1 store
                entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
                entity.Property(e => e.Description).HasMaxLength(2000);
                entity.Property(e => e.Address).HasMaxLength(500);
                entity.Property(e => e.Latitude).HasPrecision(10, 7);
                entity.Property(e => e.Longitude).HasPrecision(10, 7);
                entity.Property(e => e.AvatarUrl).HasMaxLength(1000);
                entity.Property(e => e.Phone).HasMaxLength(20);
                entity.Property(e => e.BankAccount).HasMaxLength(50);
                entity.Property(e => e.RatingScore).HasPrecision(3, 2);
                entity.Property(e => e.LicenseUrl).HasMaxLength(1000);
            });

            // SurpriseBag Configuration
            modelBuilder.Entity<SurpriseBag>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
                entity.Property(e => e.Description).HasMaxLength(2000);
                entity.Property(e => e.OriginalPrice).HasPrecision(18, 2);
                entity.Property(e => e.SalePrice).HasPrecision(18, 2);
                entity.Property(e => e.Status).IsRequired().HasMaxLength(50);

                entity.HasIndex(e => e.StoreId);
                entity.HasIndex(e => e.Status);

                entity.HasOne(e => e.Store)
                      .WithMany(s => s.SurpriseBags)
                      .HasForeignKey(e => e.StoreId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(e => e.Categories)
                      .WithMany(c => c.SurpriseBags)
                      .UsingEntity("SurpriseBagCategory"); // EF Core tự tạo join table
            });

            // StoreReview Configuration
            modelBuilder.Entity<StoreReview>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Comment).HasMaxLength(2000);
                entity.Property(e => e.StoreReply).HasMaxLength(2000);

                entity.HasIndex(e => e.StoreId);
                entity.HasIndex(e => e.BagId);
                entity.HasIndex(e => e.BuyerId);
                entity.HasIndex(e => e.OrderId).IsUnique(); // 1 order = 1 review

                entity.HasOne(e => e.Store)
                      .WithMany(s => s.StoreReviews)
                      .HasForeignKey(e => e.StoreId)
                      .OnDelete(DeleteBehavior.NoAction); // tránh cascade cycle

                entity.HasOne(e => e.Bag)
                      .WithMany(b => b.StoreReviews)
                      .HasForeignKey(e => e.BagId)
                      .OnDelete(DeleteBehavior.NoAction); // tránh cascade cycle
            });

            // OutboxMessage Configuration
            modelBuilder.Entity<OutboxMessage>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.EventType).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Payload).IsRequired();
                entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
                entity.Property(e => e.ExchangeName).IsRequired().HasMaxLength(100);
                entity.Property(e => e.ExchangeType).IsRequired().HasMaxLength(20);
                entity.Property(e => e.RoutingKey).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Error).HasMaxLength(2000);

                entity.HasIndex(e => e.Status); // query pending messages
            });
        }
    }
}
