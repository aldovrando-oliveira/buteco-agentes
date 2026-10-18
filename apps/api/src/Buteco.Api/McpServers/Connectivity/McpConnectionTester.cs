using System.Net;
using Buteco.Api.McpServers.Entities;
using Buteco.Api.McpServers.Validation;
using ModelContextProtocol.Client;

namespace Buteco.Api.McpServers.Connectivity;

/// <summary>
/// Usa o SDK oficial (<c>ModelContextProtocol.Core</c>) para conectar de
/// verdade a um servidor MCP remoto e validar a configuração via o
/// handshake <c>initialize</c>. O <see cref="HttpClient"/> usado vem de
/// <see cref="IHttpClientFactory"/> (cliente nomeado "McpConnectionTester")
/// especificamente para que testes possam substituir o
/// <see cref="HttpMessageHandler"/> primário sem rede real, mesmo espírito
/// do spy de <c>ITaskJobPublisher</c> já usado no projeto.
/// </summary>
public sealed class McpConnectionTester(IHttpClientFactory httpClientFactory) : IMcpConnectionTester
{
    public const string HttpClientName = "McpConnectionTester";

    public async Task<McpConnectionTestResult> TestAsync(string url, McpServerAuthType authType, string? credential, string? authHeaderName, CancellationToken cancellationToken)
    {
        if (RejectedCustomHeaderMessage(authType, authHeaderName) is { } rejection)
        {
            return McpConnectionTestResult.Failed(McpConnectionTestFailureReason.Unknown, rejection);
        }

        await using var transport = BuildTransport(url, authType, credential, authHeaderName);

        try
        {
            await using var client = await McpClient.CreateAsync(transport, BuildClientOptions(), cancellationToken: cancellationToken);
            return McpConnectionTestResult.Successful();
        }
        catch (HttpRequestException exception)
        {
            return exception.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? McpConnectionTestResult.Failed(McpConnectionTestFailureReason.CredentialRejected, "O servidor MCP rejeitou a credencial informada.")
                : McpConnectionTestResult.Failed(McpConnectionTestFailureReason.HostUnreachable, $"Não foi possível conectar ao servidor MCP: {exception.Message}");
        }
        catch (Exception exception)
        {
            return McpConnectionTestResult.Failed(McpConnectionTestFailureReason.Unknown, $"Falha inesperada ao testar o servidor MCP: {exception.Message}");
        }
    }

    public async Task<McpToolDiscoveryResult> ListToolsAsync(string url, McpServerAuthType authType, string? credential, string? authHeaderName, CancellationToken cancellationToken)
    {
        if (RejectedCustomHeaderMessage(authType, authHeaderName) is { } rejection)
        {
            return McpToolDiscoveryResult.Failed(McpConnectionTestFailureReason.Unknown, rejection);
        }

        await using var transport = BuildTransport(url, authType, credential, authHeaderName);

        try
        {
            await using var client = await McpClient.CreateAsync(transport, BuildClientOptions(), cancellationToken: cancellationToken);
            var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);

            return McpToolDiscoveryResult.Successful(tools
                .Select(tool => new McpToolDescriptor(tool.Name, tool.Description ?? string.Empty))
                .ToList());
        }
        catch (HttpRequestException exception)
        {
            return exception.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? McpToolDiscoveryResult.Failed(McpConnectionTestFailureReason.CredentialRejected, "O servidor MCP rejeitou a credencial informada.")
                : McpToolDiscoveryResult.Failed(McpConnectionTestFailureReason.HostUnreachable, $"Não foi possível conectar ao servidor MCP: {exception.Message}");
        }
        catch (Exception exception)
        {
            return McpToolDiscoveryResult.Failed(McpConnectionTestFailureReason.Unknown, $"Falha inesperada ao listar as tools do servidor MCP: {exception.Message}");
        }
    }

    // Checagem da regra 2 do D3 (change mcp-autenticacao-header-customizado)
    // ANTES de montar o transporte. Um nome que o .NET recusa como header de
    // requisição faz o SDK lançar InvalidOperationException com o VALOR do
    // header na mensagem (CopyAdditionalHeaders, decompilado — V2), e os catch
    // acima repassam exception.Message na resposta HTTP (V3).
    //
    // A validação de entrada de McpServerEndpoints não alcança três caminhos que
    // conectam a partir da LINHA SALVA (V9): o re-teste (POST
    // /mcp-servers/{id}/test), a descoberta de tools (GET
    // /mcp-servers/{id}/tools) e o PUT /agents/{id}/mcp-servers, que põe a
    // mensagem no `detail` de um 502. Uma linha gravada por SQL direto, ou por
    // uma validação regredida, chegaria aqui com o segredo. Esta checagem é o
    // único ponto por onde os três passam.
    //
    // Motivo Unknown, e não um motivo novo: o estado só se alcança fora da
    // validação, e um motivo novo seria contrato novo com o frontend, cujo
    // Record exaustivo de explicações sairia `undefined` (V10, D3). A mensagem
    // cita o nome (configuração) e nunca o valor.
    private static string? RejectedCustomHeaderMessage(McpServerAuthType authType, string? authHeaderName) =>
        authType == McpServerAuthType.CustomHeader && !McpAuthHeaderRules.IsAcceptedAsRequestHeader(authHeaderName)
            ? $"O nome de header salvo para este servidor ('{authHeaderName}') não pode ser usado em uma requisição. Edite o servidor e informe outro nome de header. Nenhuma requisição foi feita."
            : null;

    // Construção do transporte compartilhada entre TestAsync e ListToolsAsync
    // (Decision 3 do design.md da change backend-mcp-selecao-tools) — evita
    // duas implementações paralelas da mesma lógica de conexão MCP.
    //
    // DUPLICAÇÃO REGISTRADA, NÃO EXTRAÍDA (D6 da change
    // mcp-autenticacao-header-customizado): a montagem do header de
    // autenticação abaixo é a mesma de McpTransportFactory.BuildTransport em
    // apps/workers, e com o CustomHeader passou de um tipo para dois. Não vai
    // para libs/: os dois lados têm diferenças deliberadas (OwnsSession = false
    // só aqui; HttpClient nomeado com PooledConnectionLifetime só lá), e a #161
    // (OAuth client credentials) muda a forma da montagem — o token passa a ser
    // obtido e renovado. Gatilho para reavaliar: a #161 entregar a terceira
    // variante.
    private HttpClientTransport BuildTransport(string url, McpServerAuthType authType, string? credential, string? authHeaderName)
    {
        var transportOptions = new HttpClientTransportOptions
        {
            Endpoint = new Uri(url),
            Name = "mcp-connection-test",
            // Teste pontual: não precisa do stream GET não solicitado nem de
            // encerrar sessão no dispose (evita uma requisição DELETE extra
            // que não importa para o resultado do teste/descoberta).
            EnableStandaloneGetStream = false,
            OwnsSession = false,
        };

        transportOptions.AdditionalHeaders = authType switch
        {
            McpServerAuthType.BearerToken => new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {credential}",
            },
            // Valor cru, sem prefixo, e SEM Authorization: o SDK manda
            // AdditionalHeaders em toda requisição (POST, GET de stream, DELETE de
            // sessão, sonda server/discover — design.md, V1).
            McpServerAuthType.CustomHeader => new Dictionary<string, string>
            {
                [authHeaderName!] = credential!,
            },
            _ => null,
        };

        var httpClient = httpClientFactory.CreateClient(HttpClientName);
        return new HttpClientTransport(transportOptions, httpClient, ownsHttpClient: false);
    }

    // Bug em produção: fixar McpClientOptions.ProtocolVersion (ex.:
    // "2025-11-25") faz o SDK pedir exatamente essa versão e recusar
    // downgrade — documentado pelo próprio SDK: "the client requests
    // exactly this version and refuses to downgrade below it, throwing an
    // McpException instead of falling back". Servidores MCP reais e
    // amplamente usados ainda falam revisões mais antigas (ex.:
    // "2025-06-18"), então fixar qualquer versão única rejeita, na prática,
    // qualquer servidor de terceiros que não fale exatamente essa versão —
    // inviável para testar/conectar a um McpServer arbitrário cuja versão
    // de protocolo não se controla. Deixar ProtocolVersion nulo (default)
    // habilita o fallback automático do próprio SDK: sonda com
    // `server/discover` (revisão mais nova) e cai para o handshake
    // `initialize` clássico, negociando a versão que o servidor de fato
    // oferece, em vez de falhar.
    private static McpClientOptions BuildClientOptions() => new();
}
