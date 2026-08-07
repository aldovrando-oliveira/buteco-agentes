using System.Text.Json;
using Buteco.Api.A2A;
using Buteco.Api.AgentDelegations.Entities;
using Buteco.Api.AgentMcpBindings.Entities;
using Buteco.Api.Agents.Entities;
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
    }
}
