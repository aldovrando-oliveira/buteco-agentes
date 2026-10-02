using Buteco.Api.KnowledgeDocuments.Entities;

namespace Buteco.Api.Tests.Knowledge;

/// <summary>
/// O que <see cref="KnowledgeDocument.Update"/> devolve, sem banco nem host
/// (design.md da change historico-documentos-base, D4). São três respostas, e o
/// caso que justifica separá-las é o da linha legada: o índice precisa de
/// trabalho e o documento não mudou.
/// </summary>
public class KnowledgeDocumentUpdateOutcomeTests
{
    private const string Text = "# Política\n\nTrocas em até 30 dias.\n";

    private static KnowledgeDocument NewDocument(string title = "Documento") =>
        new(Guid.NewGuid(), title, "markdown", Text);

    [Fact]
    public void Update_WithOnlyNewText_ReportsContentChangeAndNeedsIndexing()
    {
        var document = NewDocument();

        var outcome = document.Update("Documento", "markdown", "# Outro texto\n");

        Assert.Equal(new KnowledgeDocumentUpdateOutcome(NeedsIndexing: true, ContentChanged: true, TitleChanged: false), outcome);
        Assert.True(outcome.HasDocumentChange);
    }

    [Fact]
    public void Update_WithOnlyNewTitle_ReportsTitleChangeWithoutIndexing()
    {
        var document = NewDocument("Antigo");

        var outcome = document.Update("Novo", "markdown", Text);

        Assert.Equal(new KnowledgeDocumentUpdateOutcome(NeedsIndexing: false, ContentChanged: false, TitleChanged: true), outcome);
        Assert.True(outcome.HasDocumentChange);
    }

    [Fact]
    public void Update_WithNewTextAndTitle_ReportsBoth()
    {
        var document = NewDocument("Antigo");

        var outcome = document.Update("Novo", "markdown", "# Outro texto\n");

        Assert.Equal(new KnowledgeDocumentUpdateOutcome(NeedsIndexing: true, ContentChanged: true, TitleChanged: true), outcome);
    }

    [Fact]
    public void Update_WithIdenticalTextAndTitle_ReportsNothing()
    {
        var document = NewDocument();

        var outcome = document.Update("Documento", "markdown", Text);

        Assert.Equal(new KnowledgeDocumentUpdateOutcome(NeedsIndexing: false, ContentChanged: false, TitleChanged: false), outcome);
        Assert.False(outcome.HasDocumentChange);
    }

    // Título em comparação ordinal: só caixa diferente É mudança.
    [Fact]
    public void Update_WithTitleDifferingOnlyInCase_ReportsTitleChange()
    {
        var document = NewDocument("contrato");

        var outcome = document.Update("Contrato", "markdown", Text);

        Assert.True(outcome.TitleChanged);
    }

    // Trocar só o sourceType não muda texto nem título, e não é evento.
    [Fact]
    public void Update_WithOnlyNewSourceType_ReportsNoDocumentChange()
    {
        var document = NewDocument();

        var outcome = document.Update("Documento", "outro-tipo", Text);

        Assert.False(outcome.HasDocumentChange);
    }

    /// <summary>
    /// A divergência da D4. <c>ContentHash</c> nulo é o estado das linhas
    /// anteriores ao hash, forçado aqui por reflexão porque a entidade não tem
    /// caminho público que o anule — de propósito.
    /// </summary>
    [Fact]
    public void Update_LegacyRowWithNullHashAndIdenticalText_NeedsIndexingButContentDidNotChange()
    {
        var document = NewDocument();
        typeof(KnowledgeDocument).GetProperty(nameof(KnowledgeDocument.ContentHash))!.SetValue(document, null);
        var revisionBefore = document.ContentRevision;

        var outcome = document.Update("Documento", "markdown", Text);

        Assert.True(outcome.NeedsIndexing);
        Assert.False(outcome.ContentChanged);
        Assert.False(outcome.HasDocumentChange);
        Assert.Equal(revisionBefore, document.ContentRevision);
    }

    // ContentChanged anda junto com ContentRevision, sempre — é a definição.
    [Fact]
    public void ContentChanged_MatchesTheRevisionIncrement()
    {
        var document = NewDocument();
        var revisionBefore = document.ContentRevision;

        var outcome = document.Update("Documento", "markdown", "# Outro texto\n");

        Assert.True(outcome.ContentChanged);
        Assert.Equal(revisionBefore + 1, document.ContentRevision);
    }
}
