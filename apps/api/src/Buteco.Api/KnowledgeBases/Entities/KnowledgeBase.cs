namespace Buteco.Api.KnowledgeBases.Entities;

/// <summary>
/// Base de conhecimento: um agrupamento nomeado de documentos que, a partir da
/// etapa de vínculo, pode ser ligado a N agentes.
/// </summary>
/// <remarks>
/// <see cref="Description"/> NÃO é texto decorativo de UI: na etapa de execução
/// é o texto que vira a descrição da tool exposta ao modelo, e é por ele que o
/// modelo decide se esta base é relevante para a pergunta. Por isso é
/// obrigatória e não vazia (design.md, proposal).
///
/// O tipo de conteúdo, a origem e o estado da sincronização vêm da change
/// catalogo-base-sincronizada (#102). Base manual tem todos eles nulos, exceto
/// <see cref="ContentMode"/>.
///
/// Sem exclusão, por decisão registrada em design.md (D6): é entidade de
/// catálogo, e a partir da etapa de vínculo terá agentes apontando para ela —
/// exatamente o caso que formou o padrão <c>IsActive</c> de <c>McpServer</c>.
/// </remarks>
public class KnowledgeBase
{
    public Guid Id { get; private set; }

    public string Name { get; private set; } = null!;

    public string Description { get; private set; } = null!;

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// <c>Manual</c> ou <c>Synced</c>, atribuído só na construção e nunca mais
    /// (design.md da change catalogo-base-sincronizada, D4 e D11). Não há método
    /// que o altere, e o change tracker recusa a modificação porque ele faz parte da
    /// chave alternativa que a FK composta do documento referencia.
    /// </summary>
    public KnowledgeBaseContentMode ContentMode { get; private set; }

    /// <summary>
    /// Provedor da pasta, como string aberta: o conjunto pertence ao app que
    /// sincroniza, não ao <c>apps/api</c>. Imutável. Nulo em base manual, e o
    /// banco garante a combinação.
    /// </summary>
    public string? SyncProvider { get; private set; }

    /// <summary>
    /// Id da pasta no provedor, gravado e comparado como veio, sem normalizar
    /// caixa (D5). Imutável.
    /// </summary>
    public string? SyncFolderId { get; private set; }

    /// <summary>
    /// Snapshot do nome da pasta na última sincronização concluída. O operador não
    /// o altera; só <see cref="RecordSyncSuccess"/> o atualiza (D8).
    /// </summary>
    public string? SyncFolderName { get; private set; }

    /// <inheritdoc cref="SyncFolderName"/>
    public string? SyncFolderUrl { get; private set; }

    /// <summary>
    /// Instante da última sincronização concluída com sucesso. Uma falha nunca o
    /// apaga (D2).
    /// </summary>
    public DateTimeOffset? LastSyncCompletedAt { get; private set; }

    /// <summary>
    /// Instante em que terminou o último ciclo, com sucesso ou falha. É o que muda
    /// numa segunda falha com o mesmo código, e o que a tela acompanha por polling
    /// (D2, convenção 20).
    /// </summary>
    public DateTimeOffset? LastSyncFinishedAt { get; private set; }

    /// <summary>
    /// Código estável do último erro (<c>access-denied</c>, ...), nunca frase (D1).
    /// Nulo quando a última sincronização terminou com sucesso.
    /// </summary>
    public string? LastSyncErrorCode { get; private set; }

    /// <summary>Detalhe opcional do último erro, por exemplo o e-mail da conta.</summary>
    public string? LastSyncErrorDetail { get; private set; }

    /// <summary>
    /// Desde quando a sincronização está falhando: preenchido na primeira falha
    /// depois de um sucesso, mantido nas seguintes, limpo no sucesso (D2). O banco
    /// garante que ele e <see cref="LastSyncErrorCode"/> são nulos juntos.
    /// </summary>
    public DateTimeOffset? SyncFailingSince { get; private set; }

    /// <summary>
    /// Arquivos que não entraram na base no último ciclo bem-sucedido. <b>Nulo</b>
    /// enquanto nenhum ciclo terminou com sucesso, e não lista vazia: lista vazia
    /// afirmaria que uma listagem aconteceu e não ignorou nada (D13, convenção 13).
    /// </summary>
    public IReadOnlyList<KnowledgeBaseSyncIgnoredFile>? SyncIgnoredFiles { get; private set; }

    private KnowledgeBase()
    {
    }

    public KnowledgeBase(string name, string description)
    {
        Id = Guid.NewGuid();
        Name = name;
        Description = description;
        IsActive = true;
        ContentMode = KnowledgeBaseContentMode.Manual;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>
    /// Base sincronizada, com a pasta já validada por quem acessa o provedor. Nesta
    /// change só os testes a usam; o consumidor de produção é o cadastro da #104
    /// (D11), que valida a pasta no <c>apps/connectors</c> antes de chamar aqui.
    /// </summary>
    public static KnowledgeBase CreateSynced(
        string name, string description, string provider, string folderId, string folderName, string folderUrl)
    {
        var knowledgeBase = new KnowledgeBase(name, description)
        {
            ContentMode = KnowledgeBaseContentMode.Synced,
            SyncProvider = provider,
            SyncFolderId = folderId,
            SyncFolderName = folderName,
            SyncFolderUrl = folderUrl,
        };

        return knowledgeBase;
    }

    /// <summary>
    /// Ciclo bem-sucedido (D2): última concluída e último terminado em
    /// <paramref name="now"/>, erro e "falhando desde" limpos, lista de ignorados
    /// substituída, nome e URL da pasta atualizados. Provedor e id da pasta não
    /// mudam.
    /// </summary>
    public void RecordSyncSuccess(
        DateTimeOffset now, string folderName, string folderUrl, IReadOnlyList<KnowledgeBaseSyncIgnoredFile> ignoredFiles)
    {
        EnsureSynced();

        LastSyncCompletedAt = now;
        LastSyncFinishedAt = now;
        LastSyncErrorCode = null;
        LastSyncErrorDetail = null;
        SyncFailingSince = null;
        SyncIgnoredFiles = ignoredFiles.ToList();
        SyncFolderName = folderName;
        SyncFolderUrl = folderUrl;
    }

    /// <summary>
    /// Ciclo com falha (D2): grava o erro e o último terminado, e preenche
    /// "falhando desde" só se estava nulo. <b>Não toca</b> a última concluída, a
    /// lista de ignorados (que descreve a última listagem feita, e uma falha de
    /// pasta não listou nada), nem nome e URL da pasta.
    /// </summary>
    public void RecordSyncFailure(DateTimeOffset now, string code, string? detail)
    {
        EnsureSynced();

        LastSyncFinishedAt = now;
        LastSyncErrorCode = code;
        LastSyncErrorDetail = detail;
        SyncFailingSince ??= now;
    }

    private void EnsureSynced()
    {
        if (ContentMode != KnowledgeBaseContentMode.Synced)
        {
            throw new InvalidOperationException(
                "Estado de sincronização só existe em base Synced: o chamador deveria ter recusado a base manual.");
        }
    }

    public void UpdateDetails(string name, string description)
    {
        Name = name;
        Description = description;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Activate()
    {
        IsActive = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
