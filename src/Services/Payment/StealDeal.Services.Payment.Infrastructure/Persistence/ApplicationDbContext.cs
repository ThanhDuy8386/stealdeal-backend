using Microsoft.EntityFrameworkCore;
using StealDeal.Services.Payment.Domain.Models;

namespace StealDeal.Services.Payment.Infrastructure.Persistence
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        public DbSet<Transaction> Transactions { get; set; }
        public DbSet<Refund> Refunds { get; set; }
        public DbSet<OutboxMessage> OutboxMessages { get; set; }
        public DbSet<ProcessedMessage> ProcessedMessages { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ── Transaction ───────────────────────────────────────────────────
            modelBuilder.Entity<Transaction>(entity =>
            {
                entity.HasKey(t => t.Id);

                entity.Property(t => t.Amount)
                    .HasPrecision(18, 2);

                entity.Property(t => t.PaymentMethod)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(t => t.GatewayRef)
                    .HasMaxLength(100);

                entity.Property(t => t.CheckoutUrl)
                    .HasMaxLength(2048);

                entity.Property(t => t.GatewayTransactionNo)
                    .HasMaxLength(100);

                entity.Property(t => t.GatewayResponseCode)
                    .HasMaxLength(20);

                entity.Property(t => t.GatewayTransactionStatus)
                    .HasMaxLength(20);

                entity.Property(t => t.Status)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(t => t.FailureReason)
                    .HasMaxLength(500);

                // 1:N Transaction -> Refunds
                entity.HasMany(t => t.Refunds)
                    .WithOne(r => r.Transaction)
                    .HasForeignKey(r => r.TransactionId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(t => t.OrderId);
                entity.HasIndex(t => t.UserId);
                entity.HasIndex(t => t.GatewayRef)
                    .IsUnique()
                    .HasFilter("[GatewayRef] IS NOT NULL");
            });

            // ── Refund ────────────────────────────────────────────────────────
            modelBuilder.Entity<Refund>(entity =>
            {
                entity.HasKey(r => r.Id);

                entity.Property(r => r.Amount)
                    .HasPrecision(18, 2);

                entity.Property(r => r.Reason)
                    .IsRequired()
                    .HasMaxLength(500);

                entity.Property(r => r.Status)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(r => r.GatewayRefundRef)
                    .HasMaxLength(100);

                entity.Property(r => r.GatewayResponseCode)
                    .HasMaxLength(20);

                entity.Property(r => r.FailureReason)
                    .HasMaxLength(500);

                entity.HasIndex(r => r.TransactionId);
                entity.HasIndex(r => r.OrderId);
                entity.HasIndex(r => r.GatewayRefundRef)
                    .IsUnique()
                    .HasFilter("[GatewayRefundRef] IS NOT NULL");
            });

            modelBuilder.Entity<OutboxMessage>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.EventType).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Payload).IsRequired();
                entity.Property(e => e.Status).IsRequired().HasMaxLength(50);
                entity.Property(e => e.ExchangeName).IsRequired().HasMaxLength(200);
                entity.Property(e => e.ExchangeType).IsRequired().HasMaxLength(50);
                entity.Property(e => e.RoutingKey).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Error).HasMaxLength(2000);
                entity.HasIndex(e => e.Status);
            });

            modelBuilder.Entity<ProcessedMessage>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ConsumerName).IsRequired().HasMaxLength(200);
                entity.Property(e => e.EventType).IsRequired().HasMaxLength(200);
                entity.HasIndex(e => new { e.MessageId, e.ConsumerName }).IsUnique();
            });
        }
    }
}
