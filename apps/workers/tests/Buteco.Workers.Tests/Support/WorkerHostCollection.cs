namespace Buteco.Workers.Tests.Support;

/// <summary>
/// Serializa entre si as classes de teste que sobem um <c>IHost</c> completo do
/// worker.
/// </summary>
/// <remarks>
/// Cada uma dessas classes usa <c>IClassFixture&lt;WorkerInfrastructureFixture&gt;</c>,
/// o que significa <b>containers próprios</b> de Postgres e RabbitMQ por classe.
/// Rodando em paralelo, o startup simultâneo desses containers passa do que o
/// Podman desta máquina aguenta e a classe inteira reprova na inicialização da
/// fixture — as falhas saem em bloco, no mesmo instante, e parecem falha de
/// teste em vez de falha de infraestrutura.
///
/// Medido na change dedupe-global-nome-de-tool: em `HEAD` a suíte passava
/// 132/132 em paralelo; acrescentar uma 13ª classe de host
/// (<c>AgentToolNamespaceTests</c>) fez `TaskJobConsumerTests` reprovar em bloco.
/// Serializada, a suíte fecha 168/168.
///
/// Isto é paliativo, não a correção: a correção é uma
/// <c>ICollectionFixture</c> compartilhando UM par de containers entre todas
/// essas classes, que é refatoração de toda a suíte e change própria (ver
/// "Itens em aberto" no 02-HISTORICO_E_STATUS.md).
/// </remarks>
[CollectionDefinition(Name)]
public sealed class WorkerHostCollection
{
    public const string Name = "worker-hosts";
}
