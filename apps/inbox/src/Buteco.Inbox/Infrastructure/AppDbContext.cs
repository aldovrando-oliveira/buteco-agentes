using System.Text.Json;
using Buteco.Inbox.Channels.Entities;
using Buteco.Inbox.Contacts.Entities;
using Buteco.Inbox.Messages.Entities;
using Buteco.Inbox.Orchestration.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Buteco.Inbox.Infrastructure;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Channel> Channels => Set<Channel>();

    public DbSet<Contact> Contacts => Set<Contact>();

    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<PendingDispatch> PendingDispatches => Set<PendingDispatch>();

    public DbSet<Message> Messages => Set<Message>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Channel>(entity =>
        {
            entity.ToTable("channels");
            entity.HasKey(channel => channel.Id);
            entity.Property(channel => channel.ChannelType).IsRequired();
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

            // jsonb, não colunas separadas — dicionário genérico e aberto
            // (inbox-adapter-waha, design.md, Decision 8). IReadOnlyDictionary
            // não é comparado por valor pelo EF Core por padrão; sem este
            // ValueComparer explícito, o change tracker marcaria a
            // propriedade como modificada mesmo sem mutação real.
            entity.Property(contact => contact.Metadata)
                .IsRequired()
                .HasColumnType("jsonb")
                .HasConversion(
                    metadata => JsonSerializer.Serialize(metadata, (JsonSerializerOptions?)null),
                    json => JsonSerializer.Deserialize<Dictionary<string, string>>(json, (JsonSerializerOptions?)null)!)
                .Metadata.SetValueComparer(new ValueComparer<IReadOnlyDictionary<string, string>>(
                    (left, right) => (left ?? new Dictionary<string, string>()).SequenceEqual(right ?? new Dictionary<string, string>()),
                    dictionary => dictionary.Aggregate(0, (hash, pair) => HashCode.Combine(hash, pair.Key, pair.Value)),
                    dictionary => new Dictionary<string, string>(dictionary)));

            entity.Property(contact => contact.DisplayName).IsRequired(false);

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

        modelBuilder.Entity<Message>(entity =>
        {
            entity.ToTable("messages");
            entity.HasKey(message => message.Id);
            entity.Property(message => message.SessionId).IsRequired();
            entity.Property(message => message.Direction).IsRequired().HasConversion<string>();
            entity.Property(message => message.Content).IsRequired();
            entity.Property(message => message.ContentType).IsRequired().HasConversion<string>();
            entity.Property(message => message.OccurredAt).IsRequired();
            entity.Property(message => message.ExternalId).IsRequired(false);
            entity.Property(message => message.DeliveryStatus).IsRequired(false).HasConversion<string>();
            entity.Property(message => message.DeliveryFailureReason).IsRequired(false);
            entity.Property(message => message.PendingDispatchId).IsRequired(false);
            entity.Property(message => message.DispatchStatus).IsRequired(false).HasConversion<string>();

            entity.HasOne<Session>()
                .WithMany()
                .HasForeignKey(message => message.SessionId)
                .OnDelete(DeleteBehavior.Cascade);

            // Serve "mensagens de uma Session em ordem cronológica" e,
            // reaproveitado por sessão individual, a prévia de última
            // mensagem de "sessões de um canal por última atividade"
            // (design.md, Decisões 7 e 10).
            entity.HasIndex(message => new { message.SessionId, message.OccurredAt });

            // Deduplicação de webhook reentregue — só mensagens de entrada
            // têm ExternalId (design.md, Decisão 5).
            entity.HasIndex(message => new { message.SessionId, message.ExternalId })
                .IsUnique()
                .HasFilter($"\"{nameof(Message.Direction)}\" = 'Inbound' AND \"{nameof(Message.ExternalId)}\" IS NOT NULL");
        });
    }
}
