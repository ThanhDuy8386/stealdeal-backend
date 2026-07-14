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
            });
        }
    }
}
