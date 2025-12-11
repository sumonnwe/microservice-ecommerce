using Microsoft.EntityFrameworkCore;
using OrderService.Domain.Entities;
using Shared.Domain.Entities;

namespace OrderService.Infrastructure.EF
{
    public class OrderDbContext : DbContext
    {
        public OrderDbContext(DbContextOptions<OrderDbContext> options) : base(options) { }
        public DbSet<Order> Orders => Set<Order>();
        public DbSet<OutboxEntry> OutboxEntries => Set<OutboxEntry>();
    }
}
