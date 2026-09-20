using Buteco.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests.Support;

/// <summary>
/// Insere vínculos em <c>agent_delegations</c> <b>por SQL direto</b>, sem passar
/// por <c>PUT /agents/{id}/delegations</c>.
///
/// <para>
/// É o único arranjo que produz um ciclo "herdado" — um ciclo que já está no
/// banco — nas <b>duas</b> rodadas da convenção 15: a de <c>HEAD</c>, antes da
/// correção, e a de depois dela. Montar o ciclo pela API funciona em
/// <c>HEAD</c> e passa a devolver 400 depois da correção, então um guarda
/// montado assim ficaria vermelho na segunda rodada <b>por falha de arranjo</b>,
/// não pela propriedade que ele afirma — vermelho pelo motivo errado, que é a
/// mesma armadilha que a V2 do design.md evitou em outro ponto desta change.
/// </para>
///
/// <para>
/// Mesmo idioma de <see cref="CreatedAtTie"/>, inclusive na conferência: o
/// arranjo lê de volta do banco e falha alto se o vínculo não entrou. Arranjo
/// que silenciosamente não monta o cenário deixa o guarda verde com e sem a
/// correção.
/// </para>
/// </summary>
internal static class AgentDelegationSeed
{
    public static async Task InsertAsync(IServiceProvider services, Guid sourceAgentId, Guid targetAgentId)
    {
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var affected = await dbContext.Database.ExecuteSqlRawAsync(
            "INSERT INTO agent_delegations (\"SourceAgentId\", \"TargetAgentId\") VALUES ({0}, {1}) ON CONFLICT DO NOTHING",
            sourceAgentId,
            targetAgentId);

        var present = await dbContext.Database
            .SqlQueryRaw<int>(
                "SELECT count(*)::int AS \"Value\" FROM agent_delegations WHERE \"SourceAgentId\" = {0} AND \"TargetAgentId\" = {1}",
                sourceAgentId,
                targetAgentId)
            .SingleAsync();

        Assert.True(
            present == 1,
            $"O arranjo não semeou o vínculo {sourceAgentId} → {targetAgentId} (linhas afetadas: {affected}).");
    }
}
