using Buteco.Inbox.Channels.Entities;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Inbox.Infrastructure;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Channel> Channels => Set<Channel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Channel>(entity =>
        {
            entity.ToTable("channels");
            entity.HasKey(channel => channel.Id);
            entity.Property(channel => channel.ChannelType).IsRequired().HasConversion<string>();
            entity.Property(channel => channel.Name).IsRequired();
            entity.Property(channel => channel.EncryptedCredentials).IsRequired();
            entity.Property(channel => channel.AgentId).IsRequired();
            entity.Property(channel => channel.IsActive).IsRequired().HasDefaultValue(true);
            entity.Property(channel => channel.CreatedAt).IsRequired();
            entity.Property(channel => channel.UpdatedAt).IsRequired();
        });
    }
}
