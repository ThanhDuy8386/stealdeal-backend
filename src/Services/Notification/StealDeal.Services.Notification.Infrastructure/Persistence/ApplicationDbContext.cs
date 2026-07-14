using Microsoft.EntityFrameworkCore;
using StealDeal.Services.Notification.Domain.Models;

namespace StealDeal.Services.Notification.Infrastructure.Persistence
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        public DbSet<NotificationProfile> NotificationProfiles { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<NotificationProfile>(entity =>
            {
                entity.HasKey(n => n.Id);

                entity.Property(n => n.Title)
                    .IsRequired()
                    .HasMaxLength(200);

                entity.Property(n => n.Body)
                    .IsRequired()
                    .HasMaxLength(1000);

                entity.Property(n => n.Type)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(n => n.ActionUrl)
                    .HasMaxLength(500);

                entity.Property(n => n.ReferenceType)
                    .HasMaxLength(50);

                entity.Property(n => n.IsRead)
                    .IsRequired()
                    .HasDefaultValue(false);

                entity.Property(n => n.CreatedAt)
                    .IsRequired();
            });
        }
    }
}
