using Buteco.Workers.ExecutionMetrics;
using Microsoft.Extensions.AI;

namespace Buteco.Workers.Tests.ExecutionMetrics;

/// <summary>
/// Cobre a change <c>compactacao-historico</c>, escopo 1 (D1/D2): a requisição
/// de resumo do histórico não pode terminar em mensagem de assistente.
///
/// <para>
/// <b>O que estes guardas afirmam, e o que NÃO afirmam.</b> Eles afirmam uma
/// propriedade do <b>nosso payload</b> — que a lista entregue ao provedor
/// termina em mensagem de usuário. Que o Gemini <i>aceita</i> a requisição assim
/// é evidência de <b>execução real</b>, não destes guardas: 22/09/2026, 21:15,
/// <c>America/Sao_Paulo</c>, Darwin 24.6.0, <c>Microsoft.Agents.AI</c> 1.15.0,
/// <c>Google.GenAI</c> 1.15.0, <c>gemini-3.6-flash</c>, chave de dev por
/// variável de ambiente. Antes da correção, a mesma execução devolvia
/// <c>Google.GenAI.ClientError</c> 400, <i>"Requests ending with a model turn
/// are not supported."</i>, em 256 e 281 ms. Sem este parágrafo, alguém lê o
/// guarda daqui a seis meses e supõe que ele prova o fim a fim (design.md, D8).
/// </para>
///
/// <para>
/// Sem fixture e fora da coleção de hosts: o wrapper não toca banco nem fila.
/// </para>
/// </summary>
public class CompactionCallChatClientTests
{
    [Fact]
    public async Task ListaTerminadaEmAssistente_GanhaMensagemFinalDeUsuario()
    {
        var inner = new CapturingChatClient();
        using var client = new CompactionCallChatClient(inner);

        await client.GetResponseAsync(
        [
            new ChatMessage(ChatRole.System, "You are a conversation summarizer."),
            new ChatMessage(ChatRole.User, "pergunta 1"),
            new ChatMessage(ChatRole.Assistant, "resposta 1"),
        ]);

        var enviadas = Assert.Single(inner.Calls);
        Assert.Equal(ChatRole.User, enviadas[^1].Role);
        Assert.Equal(4, enviadas.Count);
    }

    /// <summary>
    /// A forma exata que o provedor recusa é "terminar em turno de modelo", e a
    /// estratégia do pacote sempre termina assim — o laço de exclusão precisa
    /// tirar o grupo de assistente do turno para a contagem de turnos cair
    /// (`CompactionMessageIndex.cs:205,211` e `:43-45`). Por isso o caso com
    /// dois turnos, que é o que o piloto produziu no 12º turno: `[s,u,a,u,a]`.
    /// </summary>
    [Fact]
    public async Task ListaComDoisTurnos_TerminadaEmAssistente_GanhaMensagemFinalDeUsuario()
    {
        var inner = new CapturingChatClient();
        using var client = new CompactionCallChatClient(inner);

        await client.GetResponseAsync(
        [
            new ChatMessage(ChatRole.System, "You are a conversation summarizer."),
            new ChatMessage(ChatRole.User, "pergunta 1"),
            new ChatMessage(ChatRole.Assistant, "resposta 1"),
            new ChatMessage(ChatRole.User, "pergunta 2"),
            new ChatMessage(ChatRole.Assistant, "resposta 2"),
        ]);

        var enviadas = Assert.Single(inner.Calls);
        Assert.Equal(ChatRole.User, enviadas[^1].Role);
    }

    /// <summary>
    /// O par. Sem ele, o acréscimo poderia ser incondicional e este arquivo
    /// ficaria verde do mesmo jeito — e uma mensagem a mais num payload que já
    /// era válido é ruído no prompt do resumo (D1, "condicional e não
    /// incondicional").
    /// </summary>
    [Fact]
    public async Task ListaJaTerminadaEmUsuario_PassaIntacta()
    {
        var inner = new CapturingChatClient();
        using var client = new CompactionCallChatClient(inner);

        await client.GetResponseAsync(
        [
            new ChatMessage(ChatRole.System, "You are a conversation summarizer."),
            new ChatMessage(ChatRole.User, "pergunta 1"),
        ]);

        var enviadas = Assert.Single(inner.Calls);
        Assert.Equal(2, enviadas.Count);
        Assert.Equal("pergunta 1", enviadas[^1].Text);
    }

    /// <summary>
    /// O histórico é o material do resumo: mudar papel, texto ou ordem mudaria
    /// o que o modelo lê como tendo acontecido (D1, alternativas recusadas —
    /// trocar o papel da última mensagem falsificaria a autoria).
    /// </summary>
    [Fact]
    public async Task MensagensDoHistorico_NaoMudamDePapelConteudoNemOrdem()
    {
        var inner = new CapturingChatClient();
        using var client = new CompactionCallChatClient(inner);

        ChatMessage[] originais =
        [
            new(ChatRole.System, "You are a conversation summarizer."),
            new(ChatRole.User, "pergunta 1"),
            new(ChatRole.Assistant, "resposta 1"),
        ];

        await client.GetResponseAsync(originais);

        var enviadas = Assert.Single(inner.Calls);
        Assert.Collection(
            enviadas.Take(originais.Length),
            m => AssertMesmaMensagem(originais[0], m),
            m => AssertMesmaMensagem(originais[1], m),
            m => AssertMesmaMensagem(originais[2], m));
    }

    /// <summary>
    /// A razão de este wrapper existir (change <c>metricas-execucao-coleta</c>,
    /// D8) continua valendo depois da correção: sem a marca, a chamada de resumo
    /// entraria nas métricas como turno do usuário.
    /// </summary>
    [Fact]
    public async Task FinalidadeCompactacao_ContinuaMarcadaNoEscopo()
    {
        using var scope = ExecutionMetricsScope.Begin("task-compactacao");
        var inner = new CapturingChatClient();
        using var client = new CompactionCallChatClient(inner);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "pergunta 1")]);

        // Afirmado pelo caminho de produção — a linha que o client de duração
        // grava DENTRO da chamada —, e não por um leitor de finalidade que só
        // existe no escopo 5 desta change: um guarda de escopo 1 não pode
        // depender de código de outro escopo para ficar vermelho hoje.
        Assert.Equal(
            ExecutionMetricsValues.Purpose.Compaction,
            Assert.Single(scope.ProviderCalls).Purpose);
    }

    private static void AssertMesmaMensagem(ChatMessage esperada, ChatMessage recebida)
    {
        Assert.Equal(esperada.Role, recebida.Role);
        Assert.Equal(esperada.Text, recebida.Text);
    }

    private sealed class CapturingChatClient : IChatClient
    {
        public List<List<ChatMessage>> Calls { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls.Add([.. messages]);

            // Grava como o LlmCallDurationChatClient grava em produção: de
            // dentro da chamada, onde a marca de finalidade está viva.
            ExecutionMetricsScope.RecordProviderCall(
                "gemini", "gemini-3.6-flash", durationMs: 1, usage: null, failed: false, httpStatus: null);

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "resumo")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
