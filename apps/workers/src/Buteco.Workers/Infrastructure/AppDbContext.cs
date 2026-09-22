using System.Text.Json;
using Buteco.Workers.A2A;
using Buteco.Workers.AgentDelegations.Entities;
using Buteco.Workers.Agents.Entities;
using Buteco.Workers.ExecutionMetrics.Entities;
using Buteco.Workers.Knowledge.Entities;
using Buteco.Workers.Mcp.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

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

    public DbSet<McpServer> McpServers => Set<McpServer>();

    public DbSet<AgentMcpServer> AgentMcpServers => Set<AgentMcpServer>();

    public DbSet<AgentDelegation> AgentDelegations => Set<AgentDelegation>();

    public DbSet<KnowledgeBase> KnowledgeBases => Set<KnowledgeBase>();

    public DbSet<KnowledgeDocument> KnowledgeDocuments => Set<KnowledgeDocument>();

    public DbSet<AgentKnowledgeBase> AgentKnowledgeBases => Set<AgentKnowledgeBase>();

    public DbSet<KnowledgeFragment> KnowledgeFragments => Set<KnowledgeFragment>();

    public DbSet<TaskExecution> TaskExecutions => Set<TaskExecution>();

    public DbSet<ProviderCall> ProviderCalls => Set<ProviderCall>();

    public DbSet<DelegationOutcome> DelegationOutcomes => Set<DelegationOutcome>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Espelho EXATO do de apps/api, incluindo a extensão: KnowledgeSchemaMirrorTests
        // afirma que os dois modelos geram o mesmo schema.
        if (Database.IsNpgsql())
        {
            modelBuilder.HasPostgresExtension("vector");
        }

        modelBuilder.Entity<Agent>(entity =>
        {
            entity.ToTable("agents");
            entity.HasKey(agent => agent.Id);
            entity.Property(agent => agent.Name).IsRequired();
            entity.Property(agent => agent.Instructions).IsRequired();
            entity.Property(agent => agent.IsActive).IsRequired().HasDefaultValue(true);
            entity.Property(agent => agent.Provider).IsRequired(false);
            entity.Property(agent => agent.Model).IsRequired(false);
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

        modelBuilder.Entity<McpServer>(entity =>
        {
            entity.ToTable("mcp_servers");
            entity.HasKey(mcpServer => mcpServer.Id);
            entity.Property(mcpServer => mcpServer.Name).IsRequired();
            entity.Property(mcpServer => mcpServer.Description).IsRequired();
            entity.Property(mcpServer => mcpServer.Url).IsRequired();
            entity.Property(mcpServer => mcpServer.AuthType).IsRequired().HasConversion<string>();
            entity.Property(mcpServer => mcpServer.EncryptedCredential).IsRequired(false);
            entity.Property(mcpServer => mcpServer.IsActive).IsRequired().HasDefaultValue(true);
            entity.Property(mcpServer => mcpServer.CreatedAt).IsRequired();
            entity.Property(mcpServer => mcpServer.UpdatedAt).IsRequired();
        });

        modelBuilder.Entity<AgentMcpServer>(entity =>
        {
            entity.ToTable("agent_mcp_servers");
            entity.HasKey(binding => new { binding.AgentId, binding.McpServerId });
            entity.HasOne<Agent>().WithMany().HasForeignKey(binding => binding.AgentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<McpServer>().WithMany().HasForeignKey(binding => binding.McpServerId).OnDelete(DeleteBehavior.Cascade);

            // Mirror do mapeamento de apps/api (Decision 1 do design.md da
            // change backend-mcp-selecao-tools) — coluna jsonb, sem tabela
            // filha. ValueComparer explícito pelo mesmo motivo de lá:
            // IReadOnlyList<string> não tem igualdade estrutural por padrão.
            var allowedToolsProperty = entity.Property(binding => binding.AllowedTools)
                .HasColumnName("allowed_tools")
                .HasColumnType("jsonb")
                .IsRequired()
                .HasDefaultValueSql("'[]'::jsonb")
                .HasConversion(
                    tools => JsonSerializer.Serialize(tools, (JsonSerializerOptions?)null),
                    json => JsonSerializer.Deserialize<List<string>>(json, (JsonSerializerOptions?)null) ?? new List<string>());

            allowedToolsProperty.Metadata.SetValueComparer(new ValueComparer<IReadOnlyList<string>>(
                (left, right) => (left ?? new List<string>()).SequenceEqual(right ?? new List<string>()),
                tools => tools.Aggregate(0, (hash, tool) => HashCode.Combine(hash, tool.GetHashCode())),
                tools => tools.ToList()));
        });

        modelBuilder.Entity<AgentDelegation>(entity =>
        {
            entity.ToTable("agent_delegations");
            entity.HasKey(delegation => new { delegation.SourceAgentId, delegation.TargetAgentId });
            entity.HasOne<Agent>().WithMany().HasForeignKey(delegation => delegation.SourceAgentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Agent>().WithMany().HasForeignKey(delegation => delegation.TargetAgentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<KnowledgeBase>(entity =>
        {
            entity.ToTable("knowledge_bases");
            entity.HasKey(knowledgeBase => knowledgeBase.Id);
            entity.Property(knowledgeBase => knowledgeBase.Name).IsRequired();
            entity.Property(knowledgeBase => knowledgeBase.Description).IsRequired();
            entity.Property(knowledgeBase => knowledgeBase.IsActive).IsRequired().HasDefaultValue(true);
            entity.Property(knowledgeBase => knowledgeBase.CreatedAt).IsRequired();
            entity.Property(knowledgeBase => knowledgeBase.UpdatedAt).IsRequired();
        });

        modelBuilder.Entity<KnowledgeDocument>(entity =>
        {
            entity.ToTable("knowledge_documents");
            entity.HasKey(document => document.Id);
            entity.Property(document => document.Title).IsRequired();
            entity.Property(document => document.SourceType).IsRequired();
            entity.Property(document => document.ExtractedText).IsRequired();
            entity.Property(document => document.IndexingStatus).IsRequired().HasConversion<string>();
            entity.Property(document => document.IndexedAt).IsRequired(false);
            entity.Property(document => document.FailureReason).IsRequired(false);
            entity.Property(document => document.ContentRevision).IsRequired();
            entity.Property(document => document.ContentHash).IsRequired(false);
            entity.Property(document => document.FragmentCount).IsRequired().HasDefaultValue(0);
            entity.Property(document => document.IndexingAttempts).IsRequired().HasDefaultValue(0);
            entity.Property(document => document.LastAttemptAt).IsRequired(false);
            entity.Property(document => document.CreatedAt).IsRequired();
            entity.Property(document => document.UpdatedAt).IsRequired();

            // Coluna gerada pelo Postgres — precisa ser declarada igual aqui,
            // senão o modelo espelhado diverge do de apps/api e a migração
            // equivalente sairia com uma coluna comum no lugar da gerada.
            entity.Property(document => document.ContentLengthBytes)
                .HasComputedColumnSql("octet_length(\"ExtractedText\")", stored: true);

            entity.HasIndex(document => document.KnowledgeBaseId);

            // Restrict, igual a apps/api (design.md, D6).
            entity.HasOne<KnowledgeBase>()
                .WithMany()
                .HasForeignKey(document => document.KnowledgeBaseId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // O provider InMemory não sabe representar `vector` — nenhum tipo do CLR
        // mapeia para ele fora de um provider relacional. A entidade é
        // condicionada aqui por isso, e não como concessão de teste: em produção
        // e em todo teste com Testcontainers o provider é Npgsql, e o mapeamento
        // é exercitado por inteiro, inclusive por KnowledgeSchemaMirrorTests.
        //
        // Sem esta guarda, o único teste de handler que usa InMemory reprova ao
        // CONSTRUIR O MODELO — e por um motivo que não tem relação com o que ele
        // afirma, porque a validação do EF Core é do **modelo inteiro**, não da
        // entidade que o teste usa.
        if (Database.IsNpgsql())
        {
            modelBuilder.Entity<KnowledgeFragment>(entity =>
            {
                entity.ToTable("knowledge_fragments");
                entity.HasKey(fragment => fragment.Id);
                entity.Property(fragment => fragment.Text).IsRequired();
                entity.Property(fragment => fragment.Ordinal).IsRequired();
                entity.Property(fragment => fragment.EmbeddingProvider).IsRequired();
                entity.Property(fragment => fragment.EmbeddingModel).IsRequired();
                entity.Property(fragment => fragment.EmbeddingDimensions).IsRequired();
                entity.Property(fragment => fragment.CreatedAt).IsRequired();
                entity.Property(fragment => fragment.Embedding).HasColumnType("vector(4096)");

                entity.HasIndex(fragment => fragment.KnowledgeBaseId);
                entity.HasIndex(fragment => fragment.KnowledgeDocumentId);

                // Cascade — o mesmo de apps/api. Com Restrict, excluir documento
                // indexado passaria a falhar (design.md, D5).
                entity.HasOne<KnowledgeDocument>()
                    .WithMany()
                    .HasForeignKey(fragment => fragment.KnowledgeDocumentId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }
        else
        {
            // O DbSet faz o EF descobrir a entidade por convenção mesmo sem
            // mapeamento explícito — sem este Ignore, ele tenta materializar
            // `Vector` e falha por não achar construtor vinculável.
            modelBuilder.Ignore<KnowledgeFragment>();
        }


        modelBuilder.Entity<AgentKnowledgeBase>(entity =>
        {
            entity.ToTable("agent_knowledge_bases");
            entity.HasKey(binding => new { binding.AgentId, binding.KnowledgeBaseId });

            // Cascade nas duas FKs, igual a apps/api (design.md, D10) — e
            // deliberadamente diferente do Restrict que KnowledgeDocument usa
            // para a base. Se este mapeamento divergir do de lá, a migração
            // equivalente sai com regra de exclusão diferente e os dois
            // schemas deixam de bater; é o que KnowledgeSchemaMirrorTests
            // afirma.
            entity.HasOne<Agent>().WithMany().HasForeignKey(binding => binding.AgentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<KnowledgeBase>().WithMany().HasForeignKey(binding => binding.KnowledgeBaseId).OnDelete(DeleteBehavior.Cascade);
        });

        // Espelho EXATO do mapeamento de apps/api (design.md da change
        // metricas-execucao-coleta, D1) — ExecutionMetricsSchemaMirrorTests e
        // ExecutionMetricsMigrationTests afirmam o mesmo schema dos dois lados.
        // Sem FK para `agents` em nenhuma das três (D13): provedor e modelo são
        // snapshot na linha.
        modelBuilder.Entity<TaskExecution>(entity =>
        {
            entity.ToTable("task_executions");
            entity.HasKey(execution => execution.TaskId);
            entity.Property(execution => execution.ContextId).IsRequired();
            entity.Property(execution => execution.Provider).IsRequired(false);
            entity.Property(execution => execution.Model).IsRequired(false);
            entity.Property(execution => execution.Origin).IsRequired();
            entity.Property(execution => execution.SourceTaskId).IsRequired(false);
            entity.Property(execution => execution.TerminalState).IsRequired(false);
            entity.Property(execution => execution.FailurePhase).IsRequired(false);
            entity.HasIndex(execution => execution.AgentId);
            entity.HasIndex(execution => execution.StartedAt);
        });

        modelBuilder.Entity<ProviderCall>(entity =>
        {
            entity.ToTable("provider_calls");
            entity.HasKey(call => call.Id);
            entity.Property(call => call.TaskId).IsRequired();
            entity.Property(call => call.Provider).IsRequired();
            entity.Property(call => call.Model).IsRequired();
            entity.Property(call => call.Purpose).IsRequired();
            entity.HasIndex(call => call.TaskId);
            entity.HasOne<TaskExecution>().WithMany().HasForeignKey(call => call.TaskId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DelegationOutcome>(entity =>
        {
            entity.ToTable("delegation_outcomes");
            entity.HasKey(outcome => outcome.Id);
            entity.Property(outcome => outcome.SourceTaskId).IsRequired();
            entity.Property(outcome => outcome.TargetTaskId).IsRequired(false);
            entity.Property(outcome => outcome.Outcome).IsRequired();
            entity.Property(outcome => outcome.LastObservedTargetState).IsRequired(false);
            entity.HasIndex(outcome => outcome.SourceAgentId);
            entity.HasIndex(outcome => outcome.TargetAgentId);
            entity.HasOne<TaskExecution>().WithMany().HasForeignKey(outcome => outcome.SourceTaskId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
