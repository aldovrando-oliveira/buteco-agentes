using System.Text.Json;
using Buteco.Api.A2A;
using Buteco.Api.AgentDelegations.Entities;
using Buteco.Api.AgentKnowledgeBindings.Entities;
using Buteco.Api.AgentMcpBindings.Entities;
using Buteco.Api.Agents.Entities;
using Buteco.Api.KnowledgeBases.Entities;
using Buteco.Api.KnowledgeDocuments.Entities;
using Buteco.Api.McpServers.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Buteco.Api.Infrastructure;

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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Agent>(entity =>
        {
            entity.ToTable("agents");
            entity.HasKey(agent => agent.Id);
            entity.Property(agent => agent.Name).IsRequired();
            entity.Property(agent => agent.Instructions).IsRequired();
            entity.Property(agent => agent.IsActive).IsRequired().HasDefaultValue(true);
            entity.Property(agent => agent.Provider).IsRequired(false);
            entity.Property(agent => agent.Model).IsRequired(false);
            entity.Property(agent => agent.Description).IsRequired(false);
            entity.Property(agent => agent.CreatedAt).IsRequired();
            entity.Property(agent => agent.UpdatedAt).IsRequired();

            // Coluna jsonb (Decision 1 do design.md da change
            // backend-agente-description-skills) — mesmo padrão de
            // AgentMcpServer.AllowedTools: sem tabela filha, Skill não é um
            // catálogo relacional persistido em lugar nenhum. ValueComparer
            // explícito porque IReadOnlyList<Skill> não tem igualdade
            // estrutural por padrão no change tracking do EF Core (apesar de
            // Skill em si, como record, ter igualdade estrutural elemento a
            // elemento).
            var skillsProperty = entity.Property(agent => agent.Skills)
                .HasColumnName("skills")
                .HasColumnType("jsonb")
                .IsRequired()
                .HasDefaultValueSql("'[]'::jsonb")
                .HasConversion(
                    skills => JsonSerializer.Serialize(skills, (JsonSerializerOptions?)null),
                    json => JsonSerializer.Deserialize<List<Skill>>(json, (JsonSerializerOptions?)null) ?? new List<Skill>());

            skillsProperty.Metadata.SetValueComparer(new ValueComparer<IReadOnlyList<Skill>>(
                (left, right) => (left ?? new List<Skill>()).SequenceEqual(right ?? new List<Skill>()),
                skills => skills.Aggregate(0, (hash, skill) => HashCode.Combine(hash, skill.GetHashCode())),
                skills => skills.ToList()));
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

            // Coluna jsonb (Decision 1 do design.md da change
            // backend-mcp-selecao-tools) — sem tabela filha, tools não são um
            // catálogo relacional persistido em lugar nenhum. ValueComparer
            // explícito porque IReadOnlyList<string> não tem igualdade
            // estrutural por padrão, necessário para o change tracking do EF
            // Core detectar corretamente quando o conjunto de tools mudou.
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
            entity.Property(document => document.CreatedAt).IsRequired();
            entity.Property(document => document.UpdatedAt).IsRequired();

            // Coluna gerada pelo Postgres (design.md, D14):
            // GENERATED ALWAYS AS (octet_length("ExtractedText")) STORED.
            // A aplicação nunca a escreve — não há caminho de escrita de
            // conteúdo que possa deixá-la defasada, e a garantia é do banco,
            // não de disciplina. Verificado por spike: o EF gera o DDL
            // nativamente (sem SQL cru na migration) e lê o valor de volta
            // depois do INSERT e de cada UPDATE, sem Reload() explícito.
            //
            // Precisa ser octet_length e não length: length() conta
            // caracteres, e o teto validado no cadastro é em bytes UTF-8 —
            // expor uma unidade e validar outra divergiria de forma material
            // em texto acentuado (D5). O provider não tem mapeamento LINQ
            // para octet_length (confirmado por decompilação de
            // NpgsqlStringMemberTranslator), o que descarta projetá-lo na
            // consulta.
            entity.Property(document => document.ContentLengthBytes)
                .HasComputedColumnSql("octet_length(\"ExtractedText\")", stored: true);

            entity.HasIndex(document => document.KnowledgeBaseId);

            // Restrict, não o Cascade default do EF Core para FK obrigatória
            // (design.md, D6). KnowledgeBase não tem rota de exclusão; aceitar
            // Cascade por omissão deixaria a base pré-armada para o dia em que
            // alguém adicionasse uma, com todos os documentos sumindo em
            // silêncio. Restrict torna esse dia uma decisão explícita.
            entity.HasOne<KnowledgeBase>()
                .WithMany()
                .HasForeignKey(document => document.KnowledgeBaseId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AgentKnowledgeBase>(entity =>
        {
            entity.ToTable("agent_knowledge_bases");
            entity.HasKey(binding => new { binding.AgentId, binding.KnowledgeBaseId });

            // Cascade, e não o Restrict que KnowledgeDocument usa para a base
            // (design.md, D10). A distinção é entre conteúdo e vínculo: o
            // Restrict de KnowledgeDocument existe para obrigar quem for
            // adicionar exclusão de base um dia a decidir o destino dos
            // documentos, e continua valendo — enquanto houver documento, ele
            // bloqueia a exclusão da base antes de este Cascade ser alcançado.
            // Uma linha de vínculo não é conteúdo e não tem valor sem os dois
            // lados, então Cascade é o comportamento certo, e é o dos dois
            // precedentes (AgentMcpServer, AgentDelegation). Hoje as duas
            // cascatas são inertes: nem Agent nem KnowledgeBase têm rota de
            // exclusão.
            entity.HasOne<Agent>().WithMany().HasForeignKey(binding => binding.AgentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<KnowledgeBase>().WithMany().HasForeignKey(binding => binding.KnowledgeBaseId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
