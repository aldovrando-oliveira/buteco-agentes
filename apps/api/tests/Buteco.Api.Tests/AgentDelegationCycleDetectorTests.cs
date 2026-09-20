using Buteco.Api.AgentDelegations;

namespace Buteco.Api.Tests;

// G9 — guarda unitário da travessia, sem banco e sem Testcontainers. Os guardas
// de endpoint afirmam o contrato HTTP; este afirma a travessia, que é onde a
// regra de D1 vive. Separar importa: uma regra de "N saltos fixo" passaria nos
// guardas de dois saltos e reprovaria aqui, e é essa a meia-correção que o
// conjunto tem de pegar.
public class AgentDelegationCycleDetectorTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-0000000000b2");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-0000000000c3");
    private static readonly Guid D = Guid.Parse("00000000-0000-0000-0000-0000000000d4");
    private static readonly Guid E = Guid.Parse("00000000-0000-0000-0000-0000000000e5");

    [Fact]
    public void SelfDelegation_IsACycleOfOneHop()
    {
        var graph = AgentDelegationCycleDetector.BuildGraph([], A, [A]);

        Assert.Equal([A, A], AgentDelegationCycleDetector.FindCycleFrom(A, graph));
    }

    [Fact]
    public void BidirectionalPair_IsACycle()
    {
        var graph = AgentDelegationCycleDetector.BuildGraph([(B, A)], A, [B]);

        Assert.Equal([A, B, A], AgentDelegationCycleDetector.FindCycleFrom(A, graph));
    }

    [Fact]
    public void ThreeHopCycle_IsFound_AndThePathIsInOrder()
    {
        var graph = AgentDelegationCycleDetector.BuildGraph([(B, C), (C, A)], A, [B]);

        Assert.Equal([A, B, C, A], AgentDelegationCycleDetector.FindCycleFrom(A, graph));
    }

    [Fact]
    public void FourHopCycle_IsFound()
    {
        var graph = AgentDelegationCycleDetector.BuildGraph([(B, C), (C, D), (D, A)], A, [B]);

        Assert.Equal([A, B, C, D, A], AgentDelegationCycleDetector.FindCycleFrom(A, graph));
    }

    [Fact]
    public void ChainWithoutReturn_IsNotACycle()
    {
        var graph = AgentDelegationCycleDetector.BuildGraph([(B, C), (C, D)], A, [B]);

        Assert.Null(AgentDelegationCycleDetector.FindCycleFrom(A, graph));
    }

    // Caminho múltiplo, não ciclo: a alternativa recusada em D1 ("recusar
    // qualquer vínculo que crie caminho entre dois agentes já conectados")
    // reprovaria aqui.
    [Fact]
    public void DiamondWithoutCycle_IsNotACycle()
    {
        var graph = AgentDelegationCycleDetector.BuildGraph([(B, D), (C, D)], A, [B, C]);

        Assert.Null(AgentDelegationCycleDetector.FindCycleFrom(A, graph));
    }

    // Ciclo entre terceiros que não passa pela origem: não é problema DESTE
    // salvamento, e recusá-lo prenderia o operador por um ciclo que ele não está
    // criando.
    [Fact]
    public void CycleAmongOtherAgents_ThatDoesNotReachTheOrigin_IsNotReported()
    {
        var graph = AgentDelegationCycleDetector.BuildGraph([(C, D), (D, C)], A, [B]);

        Assert.Null(AgentDelegationCycleDetector.FindCycleFrom(A, graph));
    }

    // A travessia atravessa um ciclo entre terceiros sem se perder nele, e ainda
    // acha o caminho de volta à origem. Sem o `visited` global isto roda para
    // sempre.
    [Fact]
    public void CycleAmongOtherAgents_DoesNotHideACycleThatReachesTheOrigin()
    {
        var graph = AgentDelegationCycleDetector.BuildGraph([(B, C), (C, D), (D, C), (C, A)], A, [B]);

        Assert.Equal([A, B, C, A], AgentDelegationCycleDetector.FindCycleFrom(A, graph));
    }

    // O grafo é o PÓS-REPLACE: as arestas de saída da origem que estão no banco
    // são descartadas. É o que permite desfazer um ciclo herdado (D7).
    [Fact]
    public void PersistedOutgoingEdgesOfTheOrigin_AreDiscarded()
    {
        var graph = AgentDelegationCycleDetector.BuildGraph([(A, B), (B, A)], A, [C]);

        Assert.Null(AgentDelegationCycleDetector.FindCycleFrom(A, graph));
    }

    [Fact]
    public void EmptyReplacement_NeverCycles_EvenWithAnInheritedCycle()
    {
        var graph = AgentDelegationCycleDetector.BuildGraph([(A, B), (B, A)], A, []);

        Assert.Null(AgentDelegationCycleDetector.FindCycleFrom(A, graph));
    }

    [Fact]
    public void EmptyGraph_HasNoCycle()
    {
        var graph = AgentDelegationCycleDetector.BuildGraph([], A, []);

        Assert.Null(AgentDelegationCycleDetector.FindCycleFrom(A, graph));
    }

    // O ciclo pode estar no segundo alvo pedido, não no primeiro — o primeiro
    // ramo explorado não pode encerrar a busca.
    [Fact]
    public void CycleReachableOnlyThroughTheSecondTarget_IsFound()
    {
        var graph = AgentDelegationCycleDetector.BuildGraph([(B, E), (C, A)], A, [B, C]);

        Assert.Equal([A, C, A], AgentDelegationCycleDetector.FindCycleFrom(A, graph));
    }
}
