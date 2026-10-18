namespace Buteco.Workers.Mcp.Entities;

/// <summary>
/// Projeção independente da entidade <c>McpServer</c> de <c>apps/api</c>,
/// contra o mesmo schema Postgres — sem <c>ProjectReference</c> entre os dois
/// apps (mesmo padrão já usado para <see cref="Agents.Entities.Agent"/>).
/// Read-only: <c>apps/workers</c> nunca escreve nesta tabela, só lê para
/// resolver o conjunto de tools de uma execução (ver
/// <see cref="IMcpToolSetResolver"/>).
/// </summary>
public class McpServer
{
    public Guid Id { get; private set; }

    public string Name { get; private set; } = null!;

    public string Description { get; private set; } = null!;

    public string Url { get; private set; } = null!;

    // Anulável SÓ no modelo: a coluna continua NOT NULL. Nulo aqui significa
    // "valor que esta versão de apps/workers não conhece" — gravado por um
    // apps/api mais novo, num deploy fora de ordem ou depois de um rollback só
    // deste app —, nunca "ausente" (D5 da change mcp-autenticacao-header-customizado;
    // conversor em AppDbContext). O resolver exclui esse servidor sem decifrar
    // nem conectar.
    public McpServerAuthType? AuthType { get; private set; }

    // Já criptografada (ver IMcpCredentialCipher) — a entidade nunca vê o
    // valor em texto claro. Nula quando AuthType == None.
    public string? EncryptedCredential { get; private set; }

    // Nome do header do tipo CustomHeader (configuração, em claro), nulo nos
    // demais. Validado por apps/api; aqui só a regra que fecha o vazamento é
    // reconferida (ver McpToolSetResolver).
    public string? AuthHeaderName { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    private McpServer()
    {
    }
}
