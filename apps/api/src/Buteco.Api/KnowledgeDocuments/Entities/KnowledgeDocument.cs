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
    /// Escrito pelo consumidor de indexação de <c>apps/workers</c>, nunca por
    /// <c>apps/api</c>.
    /// </summary>
    public DateTimeOffset? IndexedAt { get; private set; }

    /// <summary>
    /// Motivo da última falha de indexação, em **texto legível por operador** —
    /// nunca exceção crua (design.md da change knowledge-base-indexacao, spec
    /// "Motivo de falha é legível por operador"). A tela de documentos o mostra
    /// completo, sem truncar: é a única cópia de falha que ela tem.
    ///
    /// Escrito pelo consumidor de indexação de <c>apps/workers</c>.
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

    /// <summary>
    /// SHA-256 hexadecimal de <see cref="ExtractedText"/>. Tem **um** propósito
    /// e só ele: atualização cujo conteúdo extraído seja idêntico ao gravado não
    /// volta para <see cref="KnowledgeIndexingStatus.Pending"/>, não enfileira
    /// indexação e não gasta chamada ao provedor de embedding.
    ///
    /// NÃO é chave de deduplicação entre documentos, NÃO é validação de
    /// integridade e NÃO participa de nenhuma decisão de busca.
    ///
    /// É a evolução que a etapa 1 registrou em D9 para não parecer regressão
    /// depois: lá **toda** atualização voltava a <c>Pending</c>, inclusive a que
    /// só trocava o título, e isso era conservador de propósito.
    ///
    /// Nulo em linhas anteriores a esta etapa — significa "nunca indexado sob
    /// esta regra", e a primeira atualização o preenche. Não há backfill.
    /// </summary>
    public string? ContentHash { get; private set; }

    /// <summary>
    /// Número de fragmentos gravados para este documento. Escrito pelo
    /// consumidor de indexação de <c>apps/workers</c>, nunca por <c>apps/api</c>.
    ///
    /// **Regra de exibição, que é contrato desde a etapa 1 (D8):** consumidores
    /// exibem este valor sempre que <see cref="IndexedAt"/> não for nulo,
    /// qualquer que seja o estado, e o **omitem** quando for nulo — nunca
    /// exibindo zero, que afirmaria que a indexação rodou e não achou nada.
    /// </summary>
    public int FragmentCount { get; private set; }

    /// <summary>
    /// Tentativas de indexação sobre a **revisão corrente** do conteúdo — não
    /// sobre a vida do documento. Zerado quando o conteúdo muda.
    ///
    /// Uma tentativa é uma **execução** do consumidor, não uma chamada HTTP ao
    /// provedor (design.md, D3). É o que torna verdadeiro o texto que a tela de
    /// documentos mostra: "429 nas três tentativas, a última às 03:14".
    /// </summary>
    public int IndexingAttempts { get; private set; }

    /// <inheritdoc cref="IndexingAttempts"/>
    public DateTimeOffset? LastAttemptAt { get; private set; }

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
        ContentHash = ComputeContentHash(extractedText);
        FragmentCount = 0;
        IndexingAttempts = 0;
        LastAttemptAt = null;
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
    /// mudou, e **preserva** <see cref="IndexedAt"/>.
    ///
    /// Só volta <see cref="IndexingStatus"/> para
    /// <see cref="KnowledgeIndexingStatus.Pending"/> — limpando
    /// <see cref="FailureReason"/> e zerando <see cref="IndexingAttempts"/> —
    /// quando há conteúdo novo a indexar. Atualização que só troca o título
    /// preserva o estado de indexação corrente.
    /// </summary>
    /// <returns>
    /// <c>true</c> quando há indexação a enfileirar; <c>false</c> quando o
    /// conteúdo já está indexado e nada precisa ser feito. O handler usa este
    /// retorno para decidir se publica na fila.
    /// </returns>
    public bool Update(string title, string sourceType, string extractedText)
    {
        var contentChanged = !string.Equals(ExtractedText, extractedText, StringComparison.Ordinal);

        if (contentChanged)
        {
            ContentRevision++;
        }

        Title = title;
        SourceType = sourceType;
        ExtractedText = extractedText;
        UpdatedAt = DateTimeOffset.UtcNow;

        // Só conteúdo diferente volta o documento para a fila.
        //
        // A aferição é por ContentHash, e não pela comparação de string acima,
        // por UM motivo concreto — e não porque as duas "respondam perguntas
        // diferentes", que seria falso: com o hash sempre em sincronia com o
        // texto, as duas coincidem em toda linha criada a partir desta etapa.
        //
        // Onde elas divergem é na linha LEGADA, criada antes desta change, cujo
        // ContentHash é nulo: ali a comparação de string diz "não mudou" (o
        // operador reenviou o mesmo conteúdo) enquanto o hash nulo diz "este
        // conteúdo nunca foi indexado". O hash acerta, a string erraria, e o
        // documento ficaria parado em Pending para sempre — que é exatamente o
        // estado que esta change existe para acabar.
        //
        // ContentRevision continua sendo outra coisa: é o token de descarte do
        // consumidor (etapa 1, D7), move-se com o texto e não com o hash.
        var newHash = ComputeContentHash(extractedText);
        var alreadyIndexedThisContent = string.Equals(ContentHash, newHash, StringComparison.Ordinal);
        ContentHash = newHash;

        if (alreadyIndexedThisContent)
        {
            return false;
        }

        IndexingStatus = KnowledgeIndexingStatus.Pending;
        FailureReason = null;
        IndexingAttempts = 0;
        LastAttemptAt = null;

        return true;
    }

    /// <summary>
    /// SHA-256 do texto em UTF-8, em hexadecimal minúsculo. Determinístico e
    /// estável entre processos — o mesmo conteúdo precisa dar o mesmo hash em
    /// <c>apps/api</c> hoje e daqui a um ano, senão a regra de "não reindexar
    /// conteúdo idêntico" vira reindexação silenciosa a cada deploy.
    /// </summary>
    private static string ComputeContentHash(string extractedText) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(extractedText)));
}
