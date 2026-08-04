using Buteco.Workers.Mcp.Entities;
using ModelContextProtocol.Client;

namespace Buteco.Workers.Mcp;

/// <summary>
/// Constrói o transporte MCP usado para conectar de verdade a um servidor
/// remoto — mesmo padrão de <c>McpConnectionTester.BuildTransport</c> em
/// <c>apps/api</c> (não reaproveitado, replicado — isolamento entre apps, ver
/// design.md, Estrutura de pastas proposta). Diferente de lá, não força
/// <c>OwnsSession = false</c>: aqui a conexão é uma sessão MCP de verdade,
/// que precisa encerrar corretamente (`McpToolSet.DisposeAsync`), não um
/// teste pontual descartado na mesma chamada. <c>EnableStandaloneGetStream</c>
/// continua desativado (mesmo valor de <c>apps/api</c>): esta change não
/// implementa nenhum fluxo que dependa de mensagens iniciadas pelo servidor
/// (sampling, elicitation, notificações), só chamada de tool por
/// requisição/resposta.
/// </summary>
public sealed class McpTransportFactory(IHttpClientFactory httpClientFactory)
{
    public const string HttpClientName = "McpToolExecution";

    public HttpClientTransport BuildTransport(string url, McpServerAuthType authType, string? credential, string transportName)
    {
        var transportOptions = new HttpClientTransportOptions
        {
            Endpoint = new Uri(url),
            Name = transportName,
            EnableStandaloneGetStream = false,
        };

        if (authType == McpServerAuthType.BearerToken)
        {
            transportOptions.AdditionalHeaders = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {credential}",
            };
        }

        var httpClient = httpClientFactory.CreateClient(HttpClientName);
        return new HttpClientTransport(transportOptions, httpClient, ownsHttpClient: false);
    }

    // Bug em produção: fixar McpClientOptions.ProtocolVersion (ex.:
    // "2025-11-25") faz o SDK pedir exatamente essa versão e recusar
    // downgrade — documentado pelo próprio SDK: "the client requests
    // exactly this version and refuses to downgrade below it, throwing an
    // McpException instead of falling back" (ver mesmo comentário em
    // apps/api/.../McpConnectionTester.cs, não reaproveitado — isolamento
    // entre apps). Um McpServer real vinculado a um agente pode falar
    // qualquer revisão suportada (ex.: "2025-06-18"), então fixar uma
    // versão única quebrava a execução de tool para qualquer servidor que
    // não falasse exatamente a versão fixada. Deixar ProtocolVersion nulo
    // habilita o fallback automático do SDK (sonda `server/discover`, cai
    // para `initialize` negociando a versão real do servidor).
    public static McpClientOptions BuildClientOptions() => new();
}
