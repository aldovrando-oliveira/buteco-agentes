namespace Buteco.Api.KnowledgeSync.Connectors;

/// <summary>
/// Os códigos que o próprio <c>apps/api</c> dá a uma falha na ligação com o
/// <c>apps/connectors</c> (design.md da change criacao-base-sincronizada, D1, D2 e
/// D3). O prefixo diz que a falha é entre os dois apps, e não no provedor; os códigos
/// do provedor vêm do <c>apps/connectors</c> e passam como vieram.
/// </summary>
public static class ConnectorsFailureCodes
{
    /// <summary><c>Connectors:BaseUrl</c> ausente ou vazio (D1).</summary>
    public const string NotConfigured = "connectors-not-configured";

    /// <summary>Sem resposta: conexão recusada, falha de rede ou limite de tempo (D2).</summary>
    public const string Unavailable = "connectors-unavailable";

    /// <summary>Resposta fora do contrato, inclusive 401 e 403 (D2).</summary>
    public const string Error = "connectors-error";
}
