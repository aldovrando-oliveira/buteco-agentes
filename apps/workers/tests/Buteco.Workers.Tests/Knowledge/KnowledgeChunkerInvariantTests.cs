using System.Text;
using Buteco.Workers.Knowledge.Chunking;

namespace Buteco.Workers.Tests.Knowledge;

/// <summary>
/// Os três invariantes que a spec afirma. Absolutos, sem faixa de tolerância.
///
/// <para>
/// <b>Todos os três foram vistos reprovar contra o fragmentador medido em
/// `0b`</b> — não deduzido, executado: uma reimplementação daquele algoritmo foi
/// posta no lugar desta e os guardas reprovaram, com estes números nos arranjos
/// abaixo:
/// </para>
///
/// <list type="table">
/// <item><term>I1, `.txt` corrido</term><description>legado <b>0</b> fragmentos → atual 1</description></item>
/// <item><term>I2, tabela de 60 linhas</term><description>legado maior fragmento de <b>7.226</b> caracteres em 1 pedaço → atual 1.523 em 5</description></item>
/// <item><term>I3, preâmbulo</term><description>legado descarta o trecho → atual o contém</description></item>
/// </list>
///
/// <para>
/// É a metade da convenção 15 que costuma ficar por fazer: guarda que nunca se
/// viu reprovar não vale, e um teste que sempre passou não distingue "a
/// implementação está certa" de "o arranjo não exercita nada". A
/// reimplementação era temporária e foi removida; os números ficam.
/// </para>
/// </summary>
public class KnowledgeChunkerInvariantTests
{
    private const int HardMax = 1600;

    private readonly KnowledgeChunker _chunker = new();

    // ---------------------------------------------------------------- I1 ----

    // Este é o caso mais caro, e o que menos parece caso de canto: `.txt` é
    // entrada de primeira classe declarada em KnowledgeSourceTypes ("texto puro
    // sem marcação é markdown válido"), e um .txt de política de atendimento não
    // tem cabeçalho nenhum. Com o fragmentador de `0b` ele indexava ZERO
    // fragmentos e o documento terminava `Indexed` — contagem zerada com
    // aparência de sucesso, o pior caso da convenção 13.
    [Fact]
    public void PlainTextWithoutAnyHeading_ProducesAtLeastOneFragment()
    {
        var text = string.Join("\n\n", Enumerable.Range(1, 6).Select(i =>
            $"Parágrafo {i} da política de tom de voz, escrito sem nenhuma marcação markdown."));

        var fragments = _chunker.Chunk(text);

        Assert.NotEmpty(fragments);
        AssertEveryParagraphSurvives(text, fragments);
    }

    [Fact]
    public void TitleFollowedOnlyByParagraphs_ProducesAtLeastOneFragment()
    {
        var text = "# Manual de cobrança\n\n" + string.Join("\n\n", Enumerable.Range(1, 5).Select(i =>
            $"Regra {i}: o atendente confirma identidade antes de mencionar valor."));

        var fragments = _chunker.Chunk(text);

        Assert.NotEmpty(fragments);
        AssertEveryParagraphSurvives(text, fragments);
    }

    [Fact]
    public void HeadingsThatSkipLevelTwo_AreRecognizedAsBoundaries()
    {
        var text = "# Códigos de encerramento\n\n"
                 + "### CE-01 Acordo fechado\n\nUsado quando o cliente aceitou a proposta.\n\n"
                 + "### CE-02 Promessa de pagamento\n\nO cliente se comprometeu com data.\n";

        var fragments = _chunker.Chunk(text);

        Assert.NotEmpty(fragments);
        AssertEveryParagraphSurvives(text, fragments);
        Assert.Contains(fragments, f => f.HeadingPath.Contains("CE-01 Acordo fechado"));
        Assert.Contains(fragments, f => f.HeadingPath.Contains("CE-02 Promessa de pagamento"));
    }

    [Fact]
    public void ControlDocumentWithLevelTwoHeadings_StillWorks()
    {
        var text = "# Roteiro\n\n## Abertura\n\nO atendente se identifica.\n\n## Escuta\n\nNão interrompe.\n";

        var fragments = _chunker.Chunk(text);

        Assert.NotEmpty(fragments);
        AssertEveryParagraphSurvives(text, fragments);
    }

    // ---------------------------------------------------------------- I3 ----

    // O preâmbulo — texto entre o `#` e a primeira seção — era descartado, e é o
    // defeito que só aparece no recall: a cobertura do próprio corpus de `0b`
    // ficava em 93,5% sem que nada reprovasse.
    [Fact]
    public void PreambleBetweenTitleAndFirstSection_IsIndexed()
    {
        const string preamble = "A gravação é retida por cinco anos porque esse é o prazo prescricional da dívida.";
        var text = $"# Política de retenção\n\n{preamble}\n\n## Acesso interno\n\nRestrito a supervisão.\n";

        var fragments = _chunker.Chunk(text);

        Assert.Contains(fragments, f => Normalize(f.Text).Contains(Normalize(preamble), StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- I2 ----

    [Fact]
    public void LongMarkdownTable_RespectsTheCeilingOnEmittedText()
    {
        var text = "# Integração\n\n## Tabela de códigos de retorno\n\n"
                 + "| código | significado | ação do parceiro | reprocessa? |\n|---|---|---|---|\n"
                 + string.Join('\n', Enumerable.Range(1, 60).Select(i =>
                     $"| {1000 + i} | situação number {i} descrita com texto suficiente para ocupar espaço | ação correspondente do parceiro | não |"))
                 + "\n";

        var fragments = _chunker.Chunk(text);

        Assert.NotEmpty(fragments);
        AssertNoFragmentExceedsTheCeiling(fragments);
    }

    [Fact]
    public void LongCodeBlock_RespectsTheCeilingOnEmittedText()
    {
        var text = "# Integração\n\n## Exemplo\n\n```python\n"
                 + string.Join('\n', Enumerable.Range(1, 90).Select(i =>
                     $"    resultado_{i} = consultar_debito(cpf, carteira, token, base_url, incluir_encargos=True)"))
                 + "\n```\n";

        var fragments = _chunker.Chunk(text);

        Assert.NotEmpty(fragments);
        AssertNoFragmentExceedsTheCeiling(fragments);
    }

    // Bloco denso sem tabela e sem parágrafo: uma sentença só, gigante. É o
    // caminho do corte duro, que existe para o teto ser garantia e não intenção.
    [Fact]
    public void SingleEnormousSentence_IsStillCutToTheCeiling()
    {
        var text = "# Glossário\n\n## Termos\n\n" + new string('x', HardMax * 3) + "\n";

        var fragments = _chunker.Chunk(text);

        Assert.NotEmpty(fragments);
        AssertNoFragmentExceedsTheCeiling(fragments);
    }

    // ------------------------------------------------------ tabela/cabeçalho --

    [Fact]
    public void EveryPieceOfASplitTable_CarriesTheHeaderRow()
    {
        var text = "# Integração\n\n## Tabela de códigos de retorno\n\n"
                 + "| código | significado | ação do parceiro | reprocessa? |\n|---|---|---|---|\n"
                 + string.Join('\n', Enumerable.Range(1, 60).Select(i =>
                     $"| {1000 + i} | situação number {i} descrita com texto suficiente para ocupar espaço | ação correspondente do parceiro | não |"))
                 + "\n";

        var fragments = _chunker.Chunk(text);
        var withTableRows = fragments.Where(f => f.Text.Contains("| 10", StringComparison.Ordinal)).ToList();

        Assert.True(withTableRows.Count > 1, "o arranjo precisa dividir a tabela para o teste valer");
        Assert.All(withTableRows, fragment =>
            Assert.Contains("| código | significado | ação do parceiro | reprocessa? |", fragment.Text, StringComparison.Ordinal));
    }

    // --------------------------------------------------------------- vazio --

    // O ÚNICO caso em que zero fragmentos é correto — e é propriedade do
    // fragmentador, não da guarda do consumidor. A guarda de "sucesso com zero
    // fragmentos é recusado" tem teste próprio, em
    // KnowledgeIndexingZeroFragmentGuardTests, e as duas não podem se sobrepor.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n\t  \n")]
    public void BlankInput_ProducesNoFragment(string text)
    {
        Assert.Empty(_chunker.Chunk(text));
    }

    // ------------------------------------------------------------ ajudantes --

    private static void AssertNoFragmentExceedsTheCeiling(IReadOnlyList<ChunkedFragment> fragments)
    {
        var offenders = fragments.Where(f => f.Text.Length > HardMax).ToList();

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} fragmento(s) acima do teto de {HardMax}; o maior tem {(offenders.Count == 0 ? 0 : offenders.Max(o => o.Text.Length))} caracteres");
    }

    private static void AssertEveryParagraphSurvives(string source, IReadOnlyList<ChunkedFragment> fragments)
    {
        var blob = string.Join('\n', fragments.Select(f => Normalize(f.Text)));

        foreach (var paragraph in Paragraphs(source))
        {
            Assert.True(
                blob.Contains(paragraph, StringComparison.Ordinal),
                $"parágrafo perdido na fragmentação: {paragraph[..Math.Min(80, paragraph.Length)]}…");
        }
    }

    private static IEnumerable<string> Paragraphs(string source)
    {
        foreach (var block in Normalize0(source).Split("\n\n"))
        {
            var lines = block.Split('\n').Where(line => !line.TrimStart().StartsWith('#'));
            var paragraph = Normalize(string.Join('\n', lines));
            if (paragraph.Length > 0)
            {
                yield return paragraph;
            }
        }
    }

    private static string Normalize0(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        var lastWasSpace = false;

        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                lastWasSpace = true;
                continue;
            }

            builder.Append(c);
            lastWasSpace = false;
        }

        return builder.ToString().TrimEnd();
    }
}
