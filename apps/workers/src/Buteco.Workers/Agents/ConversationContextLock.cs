using Buteco.Workers.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Workers.Agents;

/// <summary>
/// Serializa o processamento de mensagens do mesmo (agentId, contextId) entre
/// instâncias concorrentes do worker via <c>pg_advisory_lock</c> — ver
/// design.md da change apps-workers-historico-conversa, Decisão 7. Mantém uma
/// conexão Postgres dedicada aberta enquanto o lock estiver em posse, à parte
/// dos <see cref="Microsoft.Extensions.DependencyInjection.IServiceScope"/>/
/// <see cref="AppDbContext"/> de vida curta usados pelas demais operações de
/// store — sem isso, o lock (de sessão, não de transação) seria liberado
/// junto com a conexão de cada operação individual, antes do fim da seção
/// crítica. Se o processo morrer sem chamar <see cref="DisposeAsync"/>, o
/// Postgres libera o lock automaticamente ao encerrar essa conexão.
///
/// <para>
/// <b>É distribuído por construção</b>, e isso precisa estar dito porque já foi
/// afirmado o contrário: o lock vive no servidor Postgres COMPARTILHADO e
/// coordena todos os clientes conectados a ele. Verificado com duas conexões
/// independentes (exploração <c>replicas-de-worker</c>). Não é motivo para
/// limitar o número de instâncias de <c>apps/workers</c> — o que limita é
/// outra coisa, e está escrito lá.
/// </para>
///
/// <para>
/// <b>A ESPERA PELO LOCK É LIMITADA, e três coisas dependem disso.</b>
/// <c>pg_advisory_lock</c> não tem timeout próprio: quem limita é o
/// <c>CommandTimeout</c> do Npgsql, <b>30 s por default</b>, configurável pela
/// connection string. Quando estoura, <see cref="AcquireAsync"/> lança
/// <c>NpgsqlException</c> ⊃ <c>TimeoutException</c> — e é dessa garantia que
/// <c>AgentExecutionService.ExecuteAsync</c> depende para terminar a task em
/// <c>failed</c> em vez de deixá-la presa em <c>working</c> (change
/// lock-de-contexto-falha-terminal, D2).
/// <b>Uma connection string com <c>Command Timeout=0</c> reintroduz o defeito
/// em outra forma</b>: a aquisição nunca retorna, a task fica em <c>working</c>
/// para sempre, e desta vez sem nenhuma exceção para registrar. O compose de
/// produção não define esse parâmetro; se algum dia definir, esta é a linha que
/// diz o que quebra.
/// O valor de 30 s <b>não foi escolhido</b> — é o default do provider. Escolher
/// um exigiria saber quanto tempo uma conversa legitimamente segura o lock, que
/// é medição que ainda não existe.
/// </para>
///
/// <para>
/// <b>O Npgsql NÃO libera advisory lock ao devolver a conexão ao pool.</b>
/// Medido: depois de <c>Close()</c>, com o backend ainda vivo no pool, o lock
/// continua em posse e nenhuma outra conexão o consegue. Ou seja, este desenho
/// <b>depende</b> do <c>pg_advisory_unlock</c> explícito de
/// <see cref="DisposeAsync"/> — <b>fechar a conexão não basta</b>. Quem um dia
/// "simplificar" isto trocando o unlock por um simples descarte do escopo deixa
/// o lock vivo até o backend morrer, e a conversa afetada para de ser
/// processada sem nada aparecer em log.
/// </para>
///
/// <para>
/// <b><c>hashtext</c> pode colidir.</b> A chave é um par de <c>int4</c>
/// derivado de duas strings, e nada impede que dois pares (agente, contexto)
/// não relacionados caiam no mesmo par de hashes. O efeito é <b>degradação
/// silenciosa, nunca corrupção</b>: duas conversas distintas passam a se
/// serializar entre si, ficando mais lentas sem errar resultado. Se algum dia
/// aparecer serialização inexplicada entre conversas que não têm nada a ver uma
/// com a outra, é aqui que a explicação está.
/// </para>
/// </summary>
public sealed class ConversationContextLock : IAsyncDisposable
{
    private readonly IServiceScope _scope;
    private readonly AppDbContext _dbContext;
    private readonly Guid _agentId;
    private readonly string _contextId;
    private bool _released;

    private ConversationContextLock(IServiceScope scope, AppDbContext dbContext, Guid agentId, string contextId)
    {
        _scope = scope;
        _dbContext = dbContext;
        _agentId = agentId;
        _contextId = contextId;
    }

    public static async Task<ConversationContextLock> AcquireAsync(
        IServiceScopeFactory scopeFactory,
        Guid agentId,
        string contextId,
        CancellationToken cancellationToken)
    {
        var scope = scopeFactory.CreateScope();

        // O try cobre da abertura da conexão até o lock em posse. Só a partir
        // do `return` existe alguém (o ConversationContextLock devolvido) com o
        // dever de descartar este escopo; até lá, o dever é deste método.
        //
        // SEM ISTO, CADA AQUISIÇÃO QUE FALHA PENDURA UM ESCOPO COM UMA CONEXÃO
        // POSTGRES JÁ ABERTA, e ninguém a devolve ao pool. Não é teórico: com
        // o `Maximum Pool Size` default de 100, cem falhas deixam o processo
        // sem conseguir falar com o banco — nem para gravar as próprias
        // falhas, que é o caminho que chama este método. A task voltaria a
        // ficar presa em `working` pela porta oposta à que a change
        // lock-de-contexto-falha-terminal fechou. Medido com o teto baixado,
        // em RepeatedAcquisitionFailures_DoNotExhaustTheConnectionPool.
        //
        // A EXCEÇÃO É RELANÇADA COMO ESTÁ, sem embrulhar nem traduzir:
        // AgentExecutionService.ExecuteAsync loga o tipo e a mensagem dela para
        // distinguir "esperei demais pelo lock" de "o banco está fora".
        try
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            await dbContext.Database.OpenConnectionAsync(cancellationToken);
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_lock(hashtext({agentId.ToString()}), hashtext({contextId}))",
                cancellationToken);

            return new ConversationContextLock(scope, dbContext, agentId, contextId);
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_released)
        {
            _released = true;

            // Libera com CancellationToken.None de propósito: o unlock deve
            // ser tentado mesmo que o token da execução original já tenha
            // sido cancelado.
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_unlock(hashtext({_agentId.ToString()}), hashtext({_contextId}))");
            await _dbContext.Database.CloseConnectionAsync();
        }

        await _dbContext.DisposeAsync();
        _scope.Dispose();
    }
}
