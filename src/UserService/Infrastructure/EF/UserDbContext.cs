using Microsoft.EntityFrameworkCore;
using Shared.Domain.Entities;
using UserService.Domain.Entities;

namespace UserService.Infrastructure.EF
{
    // DbContext for UserService. Exposes Users and OutboxEntries so migrations create the tables.
    public class UserDbContext : DbContext
    {
        public UserDbContext(DbContextOptions<UserDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; } = null!;
        public DbSet<OutboxEntry> OutboxEntries { get; set; } = null!;
         

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<User>(static b =>
            {
                b.ToTable("Users");
                b.HasKey(x => x.Id);
                b.Property(x => x.Name).IsRequired();
                b.Property(x => x.Email).IsRequired();
                b.Property(x => x.IsActive).IsRequired();

                // LastSeenUtc / InactiveSinceUtc are optional
                b.Property(x => x.LastSeenUtc).IsRequired(false);
                b.Property(x => x.InactiveSinceUtc).IsRequired(false);

                // index to speed queries for inactive users
                b.HasIndex(x => x.InactiveSinceUtc);
                b.HasIndex(x => x.LastSeenUtc);
            });

            modelBuilder.Entity<OutboxEntry>(b =>
            {
                b.ToTable("OutboxEntries");
                b.HasKey(e => e.Id);
                b.Property(e => e.EventType).IsRequired();
                b.Property(o => o.AggregateId).IsRequired();
                b.Property(o => o.Payload).IsRequired();
                b.Property(o => o.RetryCount).HasDefaultValue(0).IsRequired();
                b.Property(o => o.CreatedAt).IsRequired();
                b.Property(o => o.SentAt).IsRequired(false);
                b.HasIndex(o => o.RetryCount);
            });
        }
    }
}
