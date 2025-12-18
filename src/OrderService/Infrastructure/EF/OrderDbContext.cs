using Microsoft.EntityFrameworkCore;
using OrderService.Domain.Entities;
using Shared.Domain.Entities;

namespace OrderService.Infrastructure.EF
{
    public class OrderDbContext : DbContext
    {
        public OrderDbContext(DbContextOptions<OrderDbContext> options) : base(options) { }

        // Use explicit get/set so EF tools and serializers behave consistently
        public DbSet<Order> Orders { get; set; } = null!;
        public DbSet<OutboxEntry> OutboxEntries { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Order>(b =>
            {
                b.ToTable("Orders");
                b.HasKey(o => o.Id);
                b.Property(o => o.UserId).IsRequired();
                b.Property(o => o.Product).IsRequired();
                b.Property(o => o.Quantity).IsRequired();
                b.Property(o => o.Price).IsRequired();
            });

            modelBuilder.Entity<OutboxEntry>(b =>
            {
                b.ToTable("OutboxEntries");
                b.HasKey(o => o.Id);
                b.Property(o => o.EventType).IsRequired();
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
