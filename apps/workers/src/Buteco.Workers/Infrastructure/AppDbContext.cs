using Buteco.Workers.A2A;
using Buteco.Workers.Agents.Entities;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Workers.Infrastructure;

/// <summary>
/// Contexto EF Core independente do de <c>apps/api</c>, apontando pro mesmo
/// schema Postgres. Só <c>apps/api</c> aplica migrations contra o banco
/// compartilhado (é quem cria o registro do agente antes de qualquer task
/// existir); este contexto nunca chama <c>Database.MigrateAsync</c> — as
/// migrations aqui existem só para geração de schema/ferramental do EF Core
/// (detectar divergência), não para serem executadas em runtime.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Agent> Agents => Set<Agent>();

    public DbSet<A2ATaskRecord> A2ATasks => Set<A2ATaskRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Agent>(entity =>
        {
            entity.ToTable("agents");
            entity.HasKey(agent => agent.Id);
            entity.Property(agent => agent.Name).IsRequired();
            entity.Property(agent => agent.Instructions).IsRequired();
            entity.Property(agent => agent.CreatedAt).IsRequired();
            entity.Property(agent => agent.UpdatedAt).IsRequired();
        });

        modelBuilder.Entity<A2ATaskRecord>(entity =>
        {
            entity.ToTable("a2a_tasks");
            entity.HasKey(task => task.TaskId);
            entity.Property(task => task.TaskId).HasColumnName("task_id");
            entity.Property(task => task.AgentId).HasColumnName("agent_id").IsRequired();
            entity.Property(task => task.ContextId).HasColumnName("context_id").IsRequired();
            entity.Property(task => task.State).HasColumnName("state").IsRequired();
            entity.Property(task => task.StatusTimestamp).HasColumnName("status_timestamp");
            entity.Property(task => task.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
            entity.HasIndex(task => task.ContextId);
            entity.HasIndex(task => task.State);
            entity.HasOne<Agent>().WithMany().HasForeignKey(task => task.AgentId);
        });
    }
}
