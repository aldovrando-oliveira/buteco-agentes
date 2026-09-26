using System.Text.Json;
using Buteco.Api.A2A;
using Buteco.Api.AgentDelegations.Entities;
using Buteco.Api.AgentKnowledgeBindings.Entities;
using Buteco.Api.AgentMcpBindings.Entities;
using Buteco.Api.Agents.Entities;
using Buteco.Api.EmbeddingMetrics.Entities;
using Buteco.Api.ExecutionMetrics.Entities;
using Buteco.Api.KnowledgeBases.Entities;
using Buteco.Api.KnowledgeDocuments.Entities;
using Buteco.Api.KnowledgeFragments.Entities;
using Buteco.Api.McpServers.Entities;
using Buteco.Api.RejectionMetrics.Entities;
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

    public DbSet<KnowledgeFragment> KnowledgeFragments => Set<KnowledgeFragment>();

    public DbSet<TaskExecution> TaskExecutions => Set<TaskExecution>();

    public DbSet<ProviderCall> ProviderCalls => Set<ProviderCall>();

    public DbSet<DelegationOutcome> DelegationOutcomes => Set<DelegationOutcome>();

    public DbSet<KnowledgeIndexingAttempt> KnowledgeIndexingAttempts => Set<KnowledgeIndexingAttempt>();

    public DbSet<EmbeddingCall> EmbeddingCalls => Set<EmbeddingCall>();

    public DbSet<TaskRejection> TaskRejections => Set<TaskRejection>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // A extensão nasce na migração de apps/api porque é apps/api quem
        // aplica migração a banco real — o migrator do compose só empacota
        // bundles de apps/api e apps/inbox (design.md, D10). apps/api NÃO
        // escreve em knowledge_fragments; quem escreve é apps/workers.
        //
        // Restrição de deploy a declarar, não a assumir: `vector` não é uma
        // extensão `trusted` (verificado: trusted = f, superuser = t), então
        // CREATE EXTENSION exige superusuário. Funciona hoje porque o migrator
        // conecta como ${POSTGRES_USER}, o superusuário de bootstrap do compose.
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
            entity.Property(document => document.ContentHash).IsRequired(false);
            entity.Property(document => document.FragmentCount).IsRequired().HasDefaultValue(0);
            entity.Property(document => document.IndexingAttempts).IsRequired().HasDefaultValue(0);
            entity.Property(document => document.LastAttemptAt).IsRequired(false);
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

                // Coluna de verdade na dimensão nativa do modelo (design.md, D1).
                // NÃO existe índice ANN aqui, e isso é decisão medida: HNSW recusa
                // mais de 2000 dimensões em `vector` e mais de 4000 em `halfvec`
                // (verificado em pgvector 0.8.6), então qualquer índice exige uma
                // coluna DERIVADA e truncada. Ela nasce por SQL quando o índice
                // fizer falta — gatilho medido: p95 da consulta acima de 200 ms —,
                // sem chamar o gateway e sem reembedar.
                //
                // ATENÇÃO ao dia de exercer esse gatilho: `ADD COLUMN ... GENERATED
                // ALWAYS AS ... STORED` REESCREVE a tabela sob ACCESS EXCLUSIVE, e
                // durante o ALTER até leitura bloqueia (medido). Para 150 mil
                // fragmentos são ~2,4 GB lidos e ~3,5 GB escritos com a tabela
                // travada. A saída medida é coluna comum (que não reescreve) mais
                // UPDATE em lotes.
                entity.Property(fragment => fragment.Embedding).HasColumnType("vector(4096)");

                // A busca da etapa 4 filtra por base e ordena por distância; este é
                // o índice que sustenta o filtro enquanto a busca for exata.
                entity.HasIndex(fragment => fragment.KnowledgeBaseId);
                entity.HasIndex(fragment => fragment.KnowledgeDocumentId);

                // Cascade, e NÃO o Restrict que KnowledgeDocument usa para a base
                // (design.md, D5). A distinção é entre conteúdo derivado e conteúdo
                // referenciado: fragmento só existe por causa do documento, e o
                // documento TEM exclusão real desde a etapa 1 (D6, o primeiro
                // MapDelete do repositório).
                //
                // Com Restrict aqui, excluir um documento já indexado passaria a
                // FALHAR — regressão direta daquela decisão. O teste que guarda
                // isso vive em apps/api, onde a exclusão mora.
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

        // Métricas de execução (design.md da change metricas-execucao-coleta,
        // D1). apps/api só migra; quem escreve é apps/workers, que espelha este
        // mapeamento — ExecutionMetricsSchemaMirrorTests confere os dois lados.
        //
        // NENHUMA das três tem FK para `agents` (D13), e isso não é descuido de
        // mapeamento: provedor e modelo são snapshot gravado na linha, e uma FK
        // convidaria a consulta a fazer join e ler o modelo de HOJE para o
        // consumo de ontem. ExecutionMetricsMigrationTests afirma a ausência.
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

        // Métricas de embedding (design.md da change metricas-embedding-coleta,
        // D1). apps/api só migra; quem escreve é apps/workers, que espelha este
        // mapeamento — EmbeddingMetricsSchemaMirrorTests confere os dois lados.
        //
        // TABELA PRÓPRIA, e não `provider_calls` com TaskId anulável: M11
        // (tokens de conversa) e M19 (embedding) são visões separadas, então
        // reusar a tabela obrigaria TODA consulta de M11 a M17 a filtrar por
        // Purpose — e quem esquecesse o filtro receberia um número maior e
        // plausível, não um erro. Isto CORRIGE D7 da etapa 1, que registrou o
        // contrário pesando só o custo de migração (convenção 9).
        //
        // NENHUMA das duas tem FK para o catálogo (D9), e não é descuido: a
        // exclusão de base cascateia para documentos e fragmentos. Com
        // Restrict, apagar uma base passaria a falhar; com Cascade, o total de
        // tokens de um período mudaria retroativamente. A métrica registra o que
        // aconteceu, e isso não muda porque o catálogo mudou depois.
        modelBuilder.Entity<KnowledgeIndexingAttempt>(entity =>
        {
            entity.ToTable("knowledge_indexing_attempts");
            entity.HasKey(attempt => attempt.Id);
            entity.Property(attempt => attempt.Outcome).IsRequired();
            entity.Property(attempt => attempt.FailurePhase).IsRequired(false);
            entity.Property(attempt => attempt.FragmentCount).IsRequired(false);
            entity.HasIndex(attempt => attempt.KnowledgeDocumentId);
            entity.HasIndex(attempt => attempt.StartedAt);
        });

        modelBuilder.Entity<EmbeddingCall>(entity =>
        {
            entity.ToTable("embedding_calls");
            entity.HasKey(call => call.Id);
            entity.Property(call => call.Purpose).IsRequired();
            entity.Property(call => call.Provider).IsRequired();
            entity.Property(call => call.Model).IsRequired();

            // Os DOIS pais são anuláveis, e exatamente um é preenchido por
            // linha — qual, depende de Purpose (D9). A indexação não roda dentro
            // de task; a busca roda, e é o único ponto em que embedding e
            // execução se encontram.
            entity.Property(call => call.KnowledgeIndexingAttemptId).IsRequired(false);
            entity.Property(call => call.TaskId).IsRequired(false);

            entity.HasIndex(call => call.KnowledgeIndexingAttemptId);
            entity.HasIndex(call => call.TaskId);

            entity.HasOne<KnowledgeIndexingAttempt>().WithMany()
                .HasForeignKey(call => call.KnowledgeIndexingAttemptId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TaskExecution>().WithMany()
                .HasForeignKey(call => call.TaskId).OnDelete(DeleteBehavior.Restrict);
        });

        // Métrica de recusa (design.md da change recusa-motivo-coleta, D2). É a
        // única tabela de métrica que apps/api ESCREVE — a recusa acontece aqui,
        // antes de qualquer publicação de job —, e por isso ela NÃO é espelhada em
        // apps/workers: ninguém lá a escreve nem a lê.
        //
        // TABELA PRÓPRIA, pelo mesmo raciocínio que decidiu embedding_calls acima:
        // uma linha em task_executions obrigaria as 22 consultas que aquela tabela
        // tem em CADA rota a filtrar a recusa, e quem esquecesse o filtro receberia
        // número maior e plausível, não erro. Uma coluna em a2a_tasks precisaria de
        // um segundo escritor da mesma linha, correndo contra o ITaskStore do SDK.
        //
        // SEM FK, e as duas razões são diferentes: para a2a_tasks, a FK viraria a
        // corrida acima em falha; para agents, a cascade que a2a_tasks já tem
        // apagaria a história de recusas no dia em que houver exclusão de agente.
        // Mesma regra das outras cinco: a métrica registra o que aconteceu, e isso
        // não muda porque o catálogo mudou depois.
        modelBuilder.Entity<TaskRejection>(entity =>
        {
            entity.ToTable("task_rejections");

            // Chave em TaskId, como task_executions: a task recusada é terminal e o
            // protocolo recusa mensagem nova para task terminal, então não há
            // segunda recusa da mesma task. Torna dupla escrita um erro visível em
            // vez de linha duplicada em silêncio.
            entity.HasKey(rejection => rejection.TaskId);

            // NOT NULL de propósito (convenção 13, do outro lado): a linha só nasce
            // num dos sítios que decidem a recusa, então não existe recusa sem
            // motivo — e é isso que faz a soma dos motivos fechar com a contagem.
            entity.Property(rejection => rejection.Reason).IsRequired();

            // Os dois índices são as duas consultas da agregação, e só elas: janela
            // (RejectedAt) e recorte por agente (AgentId). Índice composto não
            // entra sem medição, como os cinco sugeridos pela forma em
            // task_executions e embedding_calls, que também não entraram.
            entity.HasIndex(rejection => rejection.RejectedAt);
            entity.HasIndex(rejection => rejection.AgentId);
        });
    }
}
