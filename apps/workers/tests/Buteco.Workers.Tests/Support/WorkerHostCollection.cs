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
///
/// <para>
/// <b>RECALIBRAÇÃO DE 20/09/2026, com 14 classes</b> (a 14ª é
/// <c>NonTerminalTaskDetectorTests</c>, da change delegacao-diagnostico) —
/// recalibrar é tarefa de quem acrescenta a classe, não descoberta da change
/// seguinte, e esta referência já quebrou <b>duas vezes</b> por ninguém ter
/// feito isso (convenção 22).
/// </para>
/// <para>
/// <b>O critério deixou de ser o <c>uptime</c>, porque ele não discrimina nesta
/// máquina.</b> Medido nesta recalibração: a suíte fechou <b>268/268 em 6m37s
/// com load 3,10</b> na largada, e na mesma sessão reprovou <b>2 de 268 em
/// 38m07s com load 3,24</b> — carga praticamente igual, resultado e duração
/// incomparáveis. O antigo limiar (load &lt; 5,0, calibrado para 7 classes) teria
/// aprovado as duas.
/// </para>
/// <para>
/// <b>O critério recalibrado é a CONTAGEM DE CONTAINERS e a DURAÇÃO</b>, nesta
/// ordem: antes de rodar, <c>podman ps</c> tem que devolver <b>zero</b> (o stack
/// de desenvolvimento compete por memória da VM do Podman — 6 GiB, 6 CPUs); e
/// uma rodada que passe de <b>~10 min</b> não é medição, é contenção, e se
/// repete. Referência de duração: <b>6m37s com 14 classes e zero containers de
/// dev</b>. Recalibrar quando entrar a 15ª classe, ou quando a VM mudar de
/// tamanho.
/// </para>
/// </remarks>
[CollectionDefinition(Name)]
public sealed class WorkerHostCollection
{
    public const string Name = "worker-hosts";
}
