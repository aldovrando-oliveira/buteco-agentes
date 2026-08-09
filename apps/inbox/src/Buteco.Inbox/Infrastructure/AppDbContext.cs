using Buteco.Inbox.Channels.Entities;
using Buteco.Inbox.Contacts.Entities;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Inbox.Infrastructure;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Channel> Channels => Set<Channel>();

    public DbSet<Contact> Contacts => Set<Contact>();

    public DbSet<Session> Sessions => Set<Session>();

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

        modelBuilder.Entity<Contact>(entity =>
        {
            entity.ToTable("contacts");
            entity.HasKey(contact => contact.Id);
            entity.Property(contact => contact.ChannelId).IsRequired();
            entity.Property(contact => contact.ExternalId).IsRequired();
            entity.Property(contact => contact.CreatedAt).IsRequired();

            // FK real para Channel.Id — diferente de Channel.AgentId (Guid
            // opaco validado via HTTP), Channel já vive no mesmo
            // AppDbContext/banco (design.md, Decision 1). Restrict nunca
            // dispara na prática porque Channel nunca é deletado
            // fisicamente (só IsActive), é só a política mais segura por
            // padrão.
            entity.HasOne<Channel>()
                .WithMany()
                .HasForeignKey(contact => contact.ChannelId)
                .OnDelete(DeleteBehavior.Restrict);

            // Primeiro unique index do projeto (design.md, Decision 1/7) —
            // torna "find or create" coerente e é a defesa de banco contra
            // corrida em ContactSessionResolver.
            entity.HasIndex(contact => new { contact.ChannelId, contact.ExternalId }).IsUnique();
        });

        modelBuilder.Entity<Session>(entity =>
        {
            entity.ToTable("sessions");
            entity.HasKey(session => session.Id);
            entity.Property(session => session.ContactId).IsRequired();
            entity.Property(session => session.ContextId).IsRequired();
            entity.Property(session => session.StartedAt).IsRequired();
            entity.Property(session => session.LastActivityAt).IsRequired();

            entity.HasOne<Contact>()
                .WithMany()
                .HasForeignKey(session => session.ContactId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
