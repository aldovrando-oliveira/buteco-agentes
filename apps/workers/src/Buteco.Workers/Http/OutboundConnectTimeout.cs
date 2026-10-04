using System.ClientModel.Primitives;
using System.Net;

namespace Buteco.Workers.Http;

/// <summary>
/// Timeout de CONEXÃO das chamadas HTTP de saída que podem segurar o lock de
/// contexto: LLM dos três provedores, embedding e MCP (change
/// timeout-de-conexao-saida-workers, #46).
///
/// <para>
/// <b>O defeito que isto corrige.</b> Sem timeout de conexão, uma chamada cujo SYN
/// fica sem resposta (ou cujo TLS não completa) espera o timeout TOTAL do caminho,
/// vezes as retentativas do SDK, segurando o lock de contexto. A mensagem seguinte
/// da conversa desiste do lock em 30 s (<c>CommandTimeout</c>) e morre. Medido em
/// 03/10/2026 sobre <c>a7960d9</c>, contra um IPv4 sem resposta: chat OpenAI e
/// embedding 300 s (4 tentativas), Gemini 75,5 s, MCP 60,1 s, Anthropic 30,3 s. Em
/// 22/09/2026 o Gemini ficou 100 s preso assim e a mensagem seguinte falhou.
/// </para>
///
/// <para>
/// <b>O que isto NÃO conserta.</b> Com IPv6 com rota e sem conectividade (o caso de
/// 22/09), a tentativa por IPv6 consome o prazo e falha: o .NET não cai para IPv4
/// dentro dele. A chamada continua falhando, só mais rápido. O que deixa de
/// acontecer é a cascata no lock. O contorno <c>DOTNET_SYSTEM_NET_DISABLEIPV6=1</c>
/// continua necessário onde ele existe.
/// </para>
///
/// <para>
/// <b>Um handler por SDK, por processo, e nunca por execução:</b> construir handler
/// por mensagem foi o vazamento de ~44 descritores que obrigou o cache de
/// <c>ChatClientResolver</c>. Cada handler preserva o que o default daquele SDK
/// configurava, porque a mudança é o timeout de conexão, não a política de
/// redirecionamento ou de descompressão.
/// </para>
/// </summary>
public static class OutboundConnectTimeout
{
    /// <summary>
    /// 5 s por tentativa, constante e sem opção de configuração (convenção 2: não há
    /// cenário de alguém precisar de outro valor). Duas razões, com sinais opostos:
    ///
    /// <list type="number">
    /// <item>
    /// <b>O lock limita por cima.</b> O <c>System.ClientModel</c> repete a falha de
    /// conexão 4 vezes, então o chat OpenAI segura o lock ~4 × este valor. Medido em
    /// simulação: 5 s deu 20,2 s; <b>10 s deu 40,1 s</b>, acima dos 30 s em que a
    /// mensagem seguinte desiste. O limite é POR CHAMADA: uma execução com duas
    /// chamadas inalcançáveis soma os tempos e passa dos 30 s.
    /// </item>
    /// <item>
    /// <b>A conexão legítima limita por baixo.</b> O prazo cobre TCP E TLS (medido).
    /// Com o RTO inicial de SYN de 1 s (RFC 6298, lido), 5 s tolera dois SYNs perdidos
    /// mais um handshake TLS de ~2 s. Não há tempo de conexão registrado em
    /// <c>provider_calls</c>/<c>embedding_calls</c>, então este lado é argumento, não
    /// medição.
    /// </item>
    /// </list>
    ///
    /// <para>
    /// <b>Gatilho de revisão (convenção 22):</b> uma falha com
    /// <c>TimeoutException</c> "A connection could not be established within the
    /// configured ConnectTimeout" contra um provedor saudável. É a assinatura deste
    /// prazo no log, e é distinguível de qualquer outro timeout.
    /// </para>
    /// </summary>
    public static readonly TimeSpan Value = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Transporte do <c>OpenAIClient</c>, chat E embedding. Substitui o
    /// <c>HttpClientPipelineTransport.Shared</c> do <c>System.ClientModel</c>, cujo
    /// <c>HttpClient</c> padrão é <c>HttpClientHandler { AllowAutoRedirect = false }</c>
    /// com <c>Timeout</c> infinito (decompilado, 1.14.0). Os dois são mantidos.
    ///
    /// <para>
    /// <b>TEM DE SER UM POR PROCESSO.</b> <c>EmbeddingGeneratorResolver</c> constrói um
    /// <c>OpenAIClient</c> NOVO a cada chamada, e <c>KnowledgeToolSetResolver</c> e
    /// <c>KnowledgeIndexingService</c> não o descartam. Isso só é seguro porque todos
    /// esses clientes compartilham este transporte. Trocar este campo por uma
    /// construção por chamada reintroduz um pool de conexões por embedding gerado, que
    /// na tool de conhecimento é um por mensagem de agente.
    /// </para>
    /// </summary>
    public static readonly HttpClientPipelineTransport OpenAiTransport = new(
        new HttpClient(new SocketsHttpHandler
        {
            ConnectTimeout = Value,
            AllowAutoRedirect = false,
        })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        });

    // Default do SDK Anthropic 12.39.0 (ClientOptions, decompilado):
    // HttpClientHandler { AutomaticDecompression = Anthropic.Core.DecompressionMethods.Available },
    // e `Available` é GZip quando o runtime descomprime gzip (sempre, no .NET 10)
    // e None caso contrário.
    private static readonly SocketsHttpHandler AnthropicHandler = new()
    {
        ConnectTimeout = Value,
        AutomaticDecompression = DecompressionMethods.GZip,
    };

    // Default do Google.GenAI 1.15.0 (ApiClient.CreateHttpClient, decompilado):
    // `new HttpClient()`, handler sem nenhuma configuração.
    private static readonly SocketsHttpHandler GeminiHandler = new()
    {
        ConnectTimeout = Value,
    };

    /// <summary>
    /// Um <c>HttpClient</c> por <c>AnthropicClient</c>, sobre o handler do processo.
    /// <c>disposeHandler: false</c> porque o <c>AnthropicClient</c> descarta o próprio
    /// <c>HttpClient</c> (<c>ClientOptions.DisposeHttpResources</c>): sem isso, o
    /// descarte de um cliente derrubaria o handler de todos. <c>Timeout</c>
    /// infinito, como no default: quem limita a resposta é o timeout por tentativa do
    /// próprio SDK.
    /// </summary>
    public static HttpClient CreateAnthropicHttpClient() =>
        new(AnthropicHandler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>
    /// Fábrica passada em <c>Google.GenAI.Types.ClientOptions.HttpClientFactory</c>.
    /// O <c>Timeout</c> fica no default de 100 s, o mesmo do <c>new HttpClient()</c>
    /// que o SDK faria.
    /// </summary>
    public static HttpClient CreateGeminiHttpClient() =>
        new(GeminiHandler, disposeHandler: false);
}
