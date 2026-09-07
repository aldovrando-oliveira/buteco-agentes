using Buteco.Api.KnowledgeDocuments.Extraction;

namespace Buteco.Api.Tests.Knowledge;

public class MarkdownSourceExtractorTests
{
    private readonly MarkdownSourceExtractor _extractor = new();

    // A extração PRESERVA a marcação: a fragmentação da etapa de indexação
    // divide por cabeçalho, então um extrator que "limpasse" o markdown
    // quebraria a etapa seguinte sem quebrar teste nenhum desta (design.md, D11).
    [Fact]
    public void Extract_PreservesMarkdownStructure()
    {
        const string content = "# Título\n\n## Subtítulo\n\n- item um\n- item dois\n\n**negrito** e `código`\n";

        var result = _extractor.Extract(content);

        Assert.True(result.Succeeded);
        Assert.Contains("# Título", result.Text);
        Assert.Contains("## Subtítulo", result.Text);
        Assert.Contains("- item um", result.Text);
        Assert.Contains("**negrito**", result.Text);
        Assert.Contains("`código`", result.Text);
    }

    [Fact]
    public void Extract_RemovesByteOrderMarkAndNormalizesLineEndings()
    {
        const string content = "﻿# Título\r\n\r\nLinha um\rLinha dois\n";

        var result = _extractor.Extract(content);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain('﻿', result.Text!);
        Assert.DoesNotContain('\r', result.Text!);
        Assert.Equal("# Título\n\nLinha um\nLinha dois\n", result.Text);
    }

    // Uma coluna `text` do Postgres recusa U+0000 ("null character not
    // permitted"). Sem esta rejeição o erro chegaria como falha crua de banco
    // no INSERT, em vez de validação com mensagem (design.md, D11/R2).
    [Fact]
    public void Extract_WithNulCharacter_IsRejected()
    {
        var result = _extractor.Extract("# Título\n\ntexto\0com nulo\n");

        Assert.False(result.Succeeded);
        Assert.NotNull(result.FailureMessage);
        Assert.Null(result.Text);
    }

    // Par do caso acima: caracteres multibyte legítimos passam intactos.
    [Fact]
    public void Extract_WithAccentsAndEmoji_PreservesCharactersExactly()
    {
        const string content = "# Política de trocas\n\nNão há reembolso após 30 dias. 😀\n";

        var result = _extractor.Extract(content);

        Assert.True(result.Succeeded);
        Assert.Equal(content, result.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n\t  \r\n")]
    public void Extract_WithBlankContent_IsRejected(string content)
    {
        var result = _extractor.Extract(content);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.FailureMessage);
    }

    // .txt sem marcação nenhuma não é caso de erro: texto puro é markdown
    // válido, e é por isso que .txt não precisa de extrator próprio.
    [Fact]
    public void Extract_WithPlainTextAndNoMarkup_IsAccepted()
    {
        const string content = "Apenas um paragrafo simples, sem nenhuma marcacao.";

        var result = _extractor.Extract(content);

        Assert.True(result.Succeeded);
        Assert.Equal(content, result.Text);
    }
}
