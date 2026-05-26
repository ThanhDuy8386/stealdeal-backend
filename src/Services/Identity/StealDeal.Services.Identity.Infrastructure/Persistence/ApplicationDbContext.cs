using Microsoft.EntityFrameworkCore;
using StealDeal.Services.Identity.Domain.Models;

namespace StealDeal.Services.Identity.Infrastructure.Persistence
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<UserRole> UserRoles { get; set; }
        public DbSet<UserAddress> UserAddresses { get; set; }
        public DbSet<UserTrustScore> UserTrustScores { get; set; }
        public DbSet<TrustScoreEvent> TrustScoreEvents { get; set; }
        public DbSet<RefreshToken> RefreshTokens { get; set; }
        public DbSet<OauthProvider> OauthProviders { get; set; }
        public DbSet<EmailVerification> EmailVerifications { get; set; }
        public DbSet<OutboxMessage> OutboxMessages { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Users Configuration
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Email).IsRequired().HasMaxLength(256);
                entity.HasIndex(e => e.Email).IsUnique(); // Email is unique
                entity.Property(e => e.PasswordHash).IsRequired();
                entity.Property(e => e.FullName).IsRequired().HasMaxLength(256);
                entity.Property(e => e.Phone).HasMaxLength(20);
                entity.Property(e => e.AvatarUrl).HasMaxLength(1000);
            });

            // UserRoles Configuration
            modelBuilder.Entity<UserRole>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Role).IsRequired().HasMaxLength(50);
                
                // Set foreign key to Users manually
                entity.HasOne(e => e.User)
                      .WithMany(r => r.UserRoles)
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // UserAddresses Configuration
            modelBuilder.Entity<UserAddress>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Label).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Address).IsRequired().HasMaxLength(500);
                entity.Property(e => e.District).IsRequired().HasMaxLength(100);
                entity.Property(e => e.City).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Longtitude).HasColumnType("decimal(18, 6)");
                entity.Property(e => e.Latitude).HasColumnType("decimal(18, 6)");

                entity.HasOne(e => e.User)
                      .WithMany(u => u.UserAddresses)
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // UserTrustScores Configuration (1-to-1 relationship)
            modelBuilder.Entity<UserTrustScore>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.User)
                      .WithOne(u => u.UserTrustScore)
                      .HasForeignKey<UserTrustScore>(e => e.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // TrustScoreEvents Configuration
            modelBuilder.Entity<TrustScoreEvent>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.EventType).IsRequired().HasMaxLength(100);
                entity.Property(e => e.ReferenceId).HasMaxLength(100);
                entity.Property(e => e.ReferenceType).HasMaxLength(100);
                entity.Property(e => e.Note).HasMaxLength(1000);

                entity.HasOne(e => e.User)
                      .WithMany(u => u.TrustScoreEvents)
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // RefreshTokens Configuration
            modelBuilder.Entity<RefreshToken>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.TokenHash).IsRequired().HasMaxLength(500);

                entity.HasOne(e => e.User)
                      .WithMany(u => u.RefreshTokens)
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // OauthProviders Configuration
            modelBuilder.Entity<OauthProvider>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Provider).IsRequired().HasMaxLength(50);

                entity.HasOne(e => e.User)
                      .WithMany(u => u.OauthProviders)
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // EmailVerifications Configuration
            modelBuilder.Entity<EmailVerification>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.OtpHash).IsRequired().HasMaxLength(500);
                entity.Property(e => e.AttemptCount).HasDefaultValue(0);
                entity.Property(e => e.ResendCount).HasDefaultValue(0);

                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => e.ExpiresAt);

                entity.HasOne(e => e.User)
                    .WithMany(u => u.EmailVerifications)
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // OutboxMessages Configuration
            modelBuilder.Entity<OutboxMessage>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.EventType).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Payload).IsRequired();
            });
        }
    }
}
