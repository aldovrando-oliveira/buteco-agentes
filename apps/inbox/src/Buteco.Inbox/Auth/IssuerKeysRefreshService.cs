using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Buteco.Inbox.Auth;

// Duplicado deliberadamente de
// apps/connectors/src/Buteco.Connectors/Auth/IssuerKeysRefreshService.cs
// — sem libs/ (design.md da change validacao-jwks-inbox-connectors, D10). Qualquer
// mudança aqui precisa ser espelhada lá, e vice-versa; o que difere de propósito é a
// audience e a tabela de clientes.

/// <summary>
/// Obtém e renova as chaves do emissor fora do caminho da requisição (change
/// validacao-jwks-inbox-connectors, D4). Nenhuma I/O no boot: a primeira tentativa é
/// deste serviço, depois do start, e se repete a cada
/// <see cref="IssuerKeysSchedule.FirstFetchRetryInterval"/> até dar certo. Depois, a
/// cada <see cref="IssuerKeysSchedule.RefreshTickInterval"/>, chama
/// <c>GetConfigurationAsync</c>, que renova em segundo plano quando o cache passou do
/// intervalo automático (F2) e mantém as chaves anteriores se a renovação falhar.
/// <para>
/// Cada iteração captura a própria falha (convenção 4). A falha de busca já foi
/// registrada como <c>Warning</c> pelo <see cref="IssuerKeysRetriever"/>; aqui fica só
/// em <c>Debug</c>, para não duplicar a linha.
/// </para>
/// </summary>
public sealed class IssuerKeysRefreshService(
    IConfigurationManager<OpenIdConnectConfiguration> configurationManager,
    IssuerKeysState state,
    IssuerKeysSchedule schedule,
    TimeProvider timeProvider,
    ILogger<IssuerKeysRefreshService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await configurationManager.GetConfigurationAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "Tentativa de obter as chaves do emissor falhou.");
            }

            var delay = state.Obtained ? schedule.RefreshTickInterval : schedule.FirstFetchRetryInterval;
            try
            {
                await Task.Delay(delay, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
