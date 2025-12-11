using Microsoft.EntityFrameworkCore;
using UserService.Domain.Entities;
using Shared.Domain.Entities;

namespace UserService.Infrastructure.EF
{
    public class UserDbContext : DbContext
    {
        public UserDbContext(DbContextOptions<UserDbContext> options) : base(options) { }
        public DbSet<User> Users => Set<User>();
        public DbSet<OutboxEntry> OutboxEntries => Set<OutboxEntry>();
    }
}
