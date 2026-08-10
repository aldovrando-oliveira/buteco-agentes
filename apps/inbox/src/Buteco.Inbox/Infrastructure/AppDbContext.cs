using Buteco.Inbox.Channels.Entities;
using Buteco.Inbox.Contacts.Entities;
using Buteco.Inbox.Orchestration.Entities;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Inbox.Infrastructure;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Channel> Channels => Set<Channel>();

    public DbSet<Contact> Contacts => Set<Contact>();

    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<PendingDispatch> PendingDispatches => Set<PendingDispatch>();

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

        modelBuilder.Entity<PendingDispatch>(entity =>
        {
            entity.ToTable("pending_dispatches");
            entity.HasKey(dispatch => dispatch.Id);
            entity.Property(dispatch => dispatch.SessionId).IsRequired();
            entity.Property(dispatch => dispatch.Status).IsRequired().HasConversion<string>();
            entity.Property(dispatch => dispatch.LastMessageAt).IsRequired();
            entity.Property(dispatch => dispatch.AttemptCount).IsRequired().HasDefaultValue(0);

            entity.OwnsMany(dispatch => dispatch.Messages, messages => messages.ToJson());

            entity.HasOne<Session>()
                .WithMany()
                .HasForeignKey(dispatch => dispatch.SessionId)
                .OnDelete(DeleteBehavior.Cascade);

            // Só uma PendingDispatch Pending por Session — força o
            // find-or-create de InboundMessageOrchestrator a colidir em
            // vez de duplicar buffers concorrentes para a mesma sessão
            // (design.md, Riscos; mesmo padrão de defesa de banco que o
            // índice único de Contact).
            entity.HasIndex(dispatch => dispatch.SessionId)
                .IsUnique()
                .HasFilter($"\"{nameof(PendingDispatch.Status)}\" = 'Pending'");

            entity.HasIndex(dispatch => dispatch.TaskId);

            // Propriedade shadow: a convenção do provider Npgsql detecta
            // uint + IsRowVersion() + tipo de armazenamento "xid" e
            // remapeia automaticamente para a coluna de sistema real
            // "xmin" (design.md, Decisão 5 — UseXminAsConcurrencyToken()
            // não existe na versão do provider referenciada).
            entity.Property<uint>("Version").IsRowVersion();
        });
    }
}
