namespace Buteco.Api.KnowledgeDocuments.Entities;

/// <summary>
/// Documento de uma <see cref="Buteco.Api.KnowledgeBases.Entities.KnowledgeBase"/>.
/// Conteúdo, não catálogo — e é por isso que, ao contrário de toda outra
/// entidade deste repositório, tem exclusão real (design.md, D6).
/// </summary>
public class KnowledgeDocument
{
    public Guid Id { get; private set; }

    public Guid KnowledgeBaseId { get; private set; }

    public string Title { get; private set; } = null!;

    /// <summary>
    /// Identidade do extrator a aplicar, não a extensão do arquivo de origem
    /// (design.md, D12). String aberta validada em runtime contra os
    /// extratores registrados via DI keyed — mesmo idioma de
    /// <c>Channel.ChannelType</c>, nunca enum fechado.
    /// </summary>
    public string SourceType { get; private set; } = null!;

    /// <summary>
    /// Conteúdo já normalizado pelo extrator, com a marcação preservada
    /// (design.md, D11) — a fragmentação da etapa de indexação divide por
    /// cabeçalho e depende deles sobreviverem.
    /// </summary>
    public string ExtractedText { get; private set; } = null!;

    /// <summary>
    /// Tamanho de <see cref="ExtractedText"/> em bytes UTF-8. **Coluna gerada
    /// pelo Postgres** (<c>GENERATED ALWAYS AS (octet_length(...)) STORED</c>,
    /// ver <c>AppDbContext</c>): a aplicação nunca a escreve, e por isso não
    /// existe caminho — presente ou futuro — que atualize o texto e deixe o
    /// tamanho defasado (design.md, D14).
    ///
    /// Está na mesma unidade do teto validado no cadastro, e mede a mesma
    /// string que a validação mede (o texto já extraído, ver D5).
    /// </summary>
    public int ContentLengthBytes { get; private set; }

    public KnowledgeIndexingStatus IndexingStatus { get; private set; }

    /// <summary>
    /// Instante da última indexação bem-sucedida. Nulo enquanto o documento
    /// nunca foi indexado com sucesso; preenchido a partir daí e **preservado**
    /// por atualizações e por falhas de indexação (design.md, D9).
    ///
    /// É este campo, e não um valor de enum, que distingue "nunca indexado" de
    /// "há conteúdo indexado respondendo agora" (D8).
    ///
    /// Sem escritor nesta etapa: quem o preenche é o consumidor da etapa de
    /// indexação (D10).
    /// </summary>
    public DateTimeOffset? IndexedAt { get; private set; }

    /// <summary>
    /// Motivo da última falha de indexação. Sem escritor nesta etapa (D10).
    /// </summary>
    public string? FailureReason { get; private set; }

    /// <summary>
    /// Marca de revisão do conteúdo, incrementada **apenas** quando
    /// <see cref="ExtractedText"/> muda. Coluna explícita, não <c>xmin</c>
    /// (design.md, D7): o consumidor da etapa de indexação muta a própria linha
    /// ao transicionar de estado, e um token de linha invalidaria o próprio
    /// trabalho em curso — esta coluna, que ele nunca escreve, permanece
    /// estável ao longo das transições dele.
    /// </summary>
    public int ContentRevision { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    private KnowledgeDocument()
    {
    }

    public KnowledgeDocument(Guid knowledgeBaseId, string title, string sourceType, string extractedText)
    {
        Id = Guid.NewGuid();
        KnowledgeBaseId = knowledgeBaseId;
        Title = title;
        SourceType = sourceType;
        ExtractedText = extractedText;
        IndexingStatus = KnowledgeIndexingStatus.Pending;
        IndexedAt = null;
        FailureReason = null;
        ContentRevision = 1;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>
    /// Atualiza o documento com o mesmo shape do cadastro. Não distingue
    /// "editado na textarea" de "arquivo novo enviado" — nada no contrato os
    /// diferencia (design.md, D2/D9).
    ///
    /// Numa única operação: grava o conteúdo novo, incrementa
    /// <see cref="ContentRevision"/> se e somente se <see cref="ExtractedText"/>
    /// mudou, volta <see cref="IndexingStatus"/> para
    /// <see cref="KnowledgeIndexingStatus.Pending"/>, limpa
    /// <see cref="FailureReason"/> e **preserva** <see cref="IndexedAt"/>.
    ///
    /// Nesta etapa toda atualização volta a <c>Pending</c>, inclusive uma que
    /// só troque o título — é conservador de propósito; com <c>ContentHash</c>
    /// (etapa de indexação) conteúdo idêntico deixa de reenfileirar.
    /// </summary>
    public void Update(string title, string sourceType, string extractedText)
    {
        if (!string.Equals(ExtractedText, extractedText, StringComparison.Ordinal))
        {
            ContentRevision++;
        }

        Title = title;
        SourceType = sourceType;
        ExtractedText = extractedText;
        IndexingStatus = KnowledgeIndexingStatus.Pending;
        FailureReason = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
