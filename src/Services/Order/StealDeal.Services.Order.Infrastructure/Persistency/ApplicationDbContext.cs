using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StealDeal.Services.Order.Domain.Models;

namespace StealDeal.Services.Order.Infrastructure.Persistency
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        public DbSet<OrderProfile> OrderProfiles { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
        public DbSet<PickupDispute> PickupDisputes { get; set; }
        public DbSet<OutboxMessage> OutboxMessages { get; set; }
        public DbSet<ProcessedMessage> ProcessedMessages { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ── Order ─────────────────────────────────────────────────────────
            modelBuilder.Entity<OrderProfile>(entity =>
            {
                entity.HasKey(o => o.Id);

                entity.Property(o => o.StoreNameSnapshot)
                    .IsRequired()
                    .HasMaxLength(200);

                entity.Property(o => o.DeliveryFee)
                    .HasPrecision(18, 2);

                entity.Property(o => o.VoucherDiscount)
                    .HasPrecision(18, 2);

                entity.Property(o => o.TotalAmount)
                    .HasPrecision(18, 2);

                entity.Property(o => o.DeliveryType)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(o => o.DeliveryAddress)
                    .IsRequired()
                    .HasMaxLength(500);

                entity.Property(o => o.PickupCode)
                    .HasMaxLength(50);

                entity.Property(o => o.Status)
                    .IsRequired()
                    .HasMaxLength(50);

                // 1:N  Order -> OrderItems
                entity.HasMany(o => o.Items)
                    .WithOne(i => i.OrderProfile)
                    .HasForeignKey(i => i.OrderId)
                    .OnDelete(DeleteBehavior.Cascade);

                // 1:N  Order -> PickupDisputes
                entity.HasMany(o => o.Disputes)
                    .WithOne(d => d.OrderProfile)
                    .HasForeignKey(d => d.OrderId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ── OrderItem ─────────────────────────────────────────────────────
            modelBuilder.Entity<OrderItem>(entity =>
            {
                entity.HasKey(i => i.Id);

                entity.Property(i => i.BagNameSnapshot)
                    .IsRequired()
                    .HasMaxLength(200);

                entity.Property(i => i.UnitPriceSnapshot)
                    .HasPrecision(18, 2);

                entity.Property(i => i.Subtotal)
                    .HasPrecision(18, 2);
            });

            // ── PickupDispute ─────────────────────────────────────────────────
            modelBuilder.Entity<PickupDispute>(entity =>
            {
                entity.HasKey(d => d.Id);

                entity.Property(d => d.DisputeType)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(d => d.Description)
                    .IsRequired()
                    .HasMaxLength(2000);

                entity.Property(d => d.Status)
                    .IsRequired()
                    .HasMaxLength(50);

                // Serialize List<string> EvidenceUrls as JSON column
                entity.Property(d => d.EvidenceUrls)
                    .HasConversion(
                        urls => JsonSerializer.Serialize(urls, (JsonSerializerOptions?)null),
                        json => JsonSerializer.Deserialize<List<string>>(json, (JsonSerializerOptions?)null) ?? new List<string>()
                    )
                    .HasColumnType("nvarchar(max)");
            });

            // OutboxMessages Configuration
            modelBuilder.Entity<OutboxMessage>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.EventType).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Payload).IsRequired();
            });

            modelBuilder.Entity<ProcessedMessage>(entity =>
            {
                entity.HasKey(m => m.Id);

                entity.HasIndex(m => new { m.MessageId, m.ConsumerName })
                    .IsUnique();

                entity.Property(m => m.MessageId)
                    .IsRequired();

                entity.Property(m => m.ConsumerName)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(m => m.EventType)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(m => m.AggregateId);

                entity.Property(m => m.ProcessedAt)
                    .IsRequired();
            });
        }
    }
}
