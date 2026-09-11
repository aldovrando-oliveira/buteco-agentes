using System.Text;
using System.Text.RegularExpressions;

namespace Buteco.Workers.Knowledge.Chunking;

/// <summary>
/// Fragmenta o conteúdo de um documento para indexação.
///
/// <para>
/// <b>O que a spec afirma são os três invariantes</b> — documento não-vazio
/// produz ≥1 fragmento; nenhum fragmento acima do teto, medido no texto
/// emitido; todo parágrafo aparece em algum fragmento. Os parâmetros de tamanho
/// abaixo <b>não</b> estão na spec, e a distinção é deliberada: a rodada de
/// medição `0c` provou que este fragmentador não perde conteúdo, e <b>não</b>
/// provou que 900/1600 seja o ótimo — não houve braço com outra configuração de
/// tamanho. Afirmar em spec um número que a medição não otimizou é o padrão que
/// a convenção 10 nomeia (requisito que passa verde sem provar nada).
/// </para>
///
/// <para>
/// Mesmo idioma de <c>AgentDelegationToolOptions</c>: constante de produto, sem
/// seção de configuração. Pela convenção 2, a opção nasce no dia em que alguém
/// precisar de outro valor. <b>Gatilho para remedir:</b> corpus real de operador
/// com volume. Referência de `0c`: 985 caracteres por fragmento e 2,75
/// fragmentos por documento.
/// </para>
/// </summary>
public sealed partial class KnowledgeChunker : IKnowledgeChunker
{
    /// <inheritdoc cref="KnowledgeChunker"/>
    private const int TargetMin = 900;

    /// <inheritdoc cref="KnowledgeChunker"/>
    private const int HardMax = 1600;

    [GeneratedRegex(@"^(#{1,6})[ \t]+(.*\S)[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex HeadingPattern { get; }

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceBoundary { get; }

    public IReadOnlyList<ChunkedFragment> Chunk(string extractedText)
    {
        var text = Normalize(extractedText);
        var (documentTitle, sections) = ParseSections(text);

        if (sections.Count == 0)
        {
            // DEFEITO (a) DA MEDIÇÃO `0b`, e o mais caro dos três: o
            // fragmentador anterior só reconhecia `##` e DESCARTAVA tudo o que
            // viesse antes do primeiro, então documento sem `##` produzia ZERO
            // fragmentos em silêncio e terminava `Indexed`.
            //
            // Não é caso de canto: `.txt` é entrada de primeira classe declarada
            // em KnowledgeSourceTypes ("texto puro sem marcação é markdown
            // válido"), e um .txt de política não tem cabeçalho nenhum. Medido:
            // 3 de 40 documentos do corpus de `0c` caíam aqui.
            var bare = StripHeadingLines(text);
            if (string.IsNullOrWhiteSpace(bare))
            {
                return [];
            }

            sections = [new Section([], bare)];
        }

        var fragments = new List<ChunkedFragment>();
        var bufferPath = new List<string>();
        var buffer = new StringBuilder();

        void Flush()
        {
            if (buffer.Length > 0 && !string.IsNullOrWhiteSpace(buffer.ToString()))
            {
                Emit(fragments, documentTitle, bufferPath, buffer.ToString());
            }

            bufferPath = [];
            buffer.Clear();
        }

        foreach (var section in sections)
        {
            var sectionTitle = section.Path.Count > 0 ? section.Path[^1] : null;
            var piece = sectionTitle is null ? section.Body : $"### {sectionTitle}\n{section.Body}";

            // O orçamento desconta o prefixo de caminho que será injetado no
            // Emit — DEFEITO (b)/(c) da medição: o fragmentador anterior media
            // o corpo e concatenava o prefixo depois, então o teto não era teto.
            var budget = HardMax - PrefixFor(documentTitle, section.Path).Length - 2;

            if (piece.Length > budget)
            {
                Flush();
                foreach (var part in Split(piece, budget))
                {
                    Emit(fragments, documentTitle, section.Path, part);
                }

                continue;
            }

            var mergedPath = Merge(bufferPath, section.Path);
            var candidate = buffer.Length == 0 ? piece : $"{buffer}\n\n{piece}";
            var candidateBudget = HardMax - PrefixFor(documentTitle, mergedPath).Length - 2;

            if (candidate.Length > candidateBudget)
            {
                Flush();
                bufferPath = [.. section.Path];
                buffer.Append(piece);
            }
            else
            {
                bufferPath = mergedPath;
                buffer.Clear();
                buffer.Append(candidate);
            }

            if (buffer.Length >= TargetMin)
            {
                Flush();
            }
        }

        Flush();
        return fragments;
    }

    /// <summary>
    /// Espelha o extrator de markdown de <c>apps/api</c>: BOM e fim de linha.
    /// Não "limpa" marcação — a etapa 1 decidiu preservá-la (D11) justamente
    /// porque a fragmentação depende dos cabeçalhos sobreviverem.
    /// </summary>
    private static string Normalize(string text)
    {
        if (text.Length > 0 && text[0] == '﻿')
        {
            text = text[1..];
        }

        return text.Replace("\r\n", "\n").Replace('\r', '\n');
    }

    private static string StripHeadingLines(string text) =>
        string.Join('\n', text.Split('\n').Where(line => !HeadingPattern.IsMatch(line))).Trim();

    private static string PrefixFor(string? documentTitle, IReadOnlyList<string> path)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(documentTitle))
        {
            parts.Add(documentTitle);
        }

        parts.AddRange(path.Where(segment => !string.IsNullOrWhiteSpace(segment)));
        return string.Join(" > ", parts);
    }

    private static List<string> Merge(IReadOnlyList<string> current, IReadOnlyList<string> incoming)
    {
        var merged = new List<string>(current);
        merged.AddRange(incoming.Where(segment => !merged.Contains(segment, StringComparer.Ordinal)));
        return merged;
    }

    private static void Emit(List<ChunkedFragment> fragments, string? documentTitle, IReadOnlyList<string> path, string body)
    {
        var prefix = PrefixFor(documentTitle, path);
        var text = (prefix.Length == 0 ? body : $"{prefix}\n\n{body}").Trim();

        if (text.Length > 0)
        {
            fragments.Add(new ChunkedFragment(fragments.Count, text, [.. path]));
        }
    }

    /// <summary>
    /// Divide <paramref name="text"/> em pedaços de no máximo
    /// <paramref name="budget"/> caracteres, descendo por níveis: parágrafo →
    /// linha → sentença → corte duro.
    ///
    /// <para>
    /// O nível de <b>linha</b> é o que trata tabela markdown e bloco de código,
    /// que são um parágrafo só — foi exatamente ali que o fragmentador anterior
    /// emitiu 2.858 caracteres contra um teto de 1.600, porque quebrava só por
    /// parágrafo e um parágrafo que sozinho estourasse saía inteiro. O corte
    /// duro existe para que o teto seja garantia, não intenção.
    /// </para>
    /// </summary>
    private static List<string> Split(string text, int budget)
    {
        if (text.Length <= budget)
        {
            return [text];
        }

        var paragraphs = Join([.. text.Split("\n\n").Where(p => !string.IsNullOrWhiteSpace(p))], "\n\n", budget);
        if (paragraphs.All(p => p.Length <= budget))
        {
            return paragraphs;
        }

        var result = new List<string>();
        foreach (var paragraph in paragraphs)
        {
            if (paragraph.Length <= budget)
            {
                result.Add(paragraph);
                continue;
            }

            var table = TableHeaderOf(paragraph);
            if (table is not null)
            {
                result.AddRange(SplitTable(table.Value, budget));
                continue;
            }

            foreach (var line in Join([.. paragraph.Split('\n')], "\n", budget))
            {
                if (line.Length <= budget)
                {
                    result.Add(line);
                    continue;
                }

                foreach (var sentence in Join([.. SentenceBoundary.Split(line)], " ", budget))
                {
                    if (sentence.Length <= budget)
                    {
                        result.Add(sentence);
                        continue;
                    }

                    for (var i = 0; i < sentence.Length; i += budget)
                    {
                        result.Add(sentence.Substring(i, Math.Min(budget, sentence.Length - i)));
                    }
                }
            }
        }

        return result;
    }

    private static List<string> Join(IReadOnlyList<string> pieces, string separator, int budget)
    {
        var result = new List<string>();
        var current = string.Empty;

        foreach (var piece in pieces)
        {
            var candidate = current.Length == 0 ? piece : current + separator + piece;
            if (candidate.Length > budget && current.Length > 0)
            {
                result.Add(current);
                current = piece;
            }
            else
            {
                current = candidate;
            }
        }

        if (current.Length > 0)
        {
            result.Add(current);
        }

        return result;
    }

    /// <summary>
    /// Acha o cabeçalho de uma tabela markdown dentro do bloco. Ele não está
    /// necessariamente na primeira linha: a peça costuma começar com o título da
    /// seção antes da tabela.
    /// </summary>
    private static (string Preamble, string Header, string[] Rows)? TableHeaderOf(string block)
    {
        var lines = block.Split('\n');
        for (var i = 0; i < lines.Length - 1; i++)
        {
            var candidate = lines[i].TrimStart();
            var separator = lines[i + 1].Trim();
            if (!candidate.StartsWith('|') || separator.Length == 0)
            {
                continue;
            }

            if (separator.Replace("|", string.Empty).Replace(" ", string.Empty).All(c => c is '-' or ':'))
            {
                return (string.Join('\n', lines[..i]), lines[i] + "\n" + lines[i + 1], lines[(i + 2)..]);
            }
        }

        return null;
    }

    /// <summary>
    /// Divide uma tabela repetindo o cabeçalho em cada pedaço.
    ///
    /// <para>
    /// <b>Mitigação medida, não estética.</b> Sem repetir o cabeçalho, a
    /// continuação vira uma laje de linhas sem prosa e sem os nomes das colunas,
    /// cujo vetor fica perto do centroide do vocabulário do domínio e passa a
    /// vencer a busca em consultas com que não tem relação. `0c` mediu um único
    /// fragmento assim sendo o topo de <b>32 de 83 consultas</b>; repetindo o
    /// cabeçalho cai para 10.
    /// </para>
    ///
    /// <para>
    /// <b>É mitigação PARCIAL, e isso importa:</b> 12% ainda é mais de treze
    /// vezes o ~0,9% de um índice uniforme. A causa raiz é bloco denso e
    /// heterogêneo virar vetor de centroide, e vale para glossário, lista de
    /// códigos em texto corrido ou índice remissivo — onde não há cabeçalho para
    /// repetir. Não leia esta correção como solução.
    /// </para>
    /// </summary>
    private static List<string> SplitTable((string Preamble, string Header, string[] Rows) table, int budget)
    {
        var baseText = string.IsNullOrWhiteSpace(table.Preamble)
            ? table.Header
            : table.Preamble + "\n" + table.Header;

        var result = new List<string>();
        var current = string.Empty;

        foreach (var row in table.Rows)
        {
            var candidate = current.Length == 0 ? row : current + "\n" + row;
            if (baseText.Length + 1 + candidate.Length > budget && current.Length > 0)
            {
                result.Add(baseText + "\n" + current);
                current = row;
            }
            else
            {
                current = candidate;
            }
        }

        if (current.Length > 0)
        {
            result.Add(baseText + "\n" + current);
        }

        return result;
    }

    private readonly record struct Section(List<string> Path, string Body);

    /// <summary>
    /// Divide em seções por cabeçalho de qualquer nível de 2 a 6, mantendo o
    /// caminho completo, e <b>captura o preâmbulo</b> — o texto entre o título
    /// de nível 1 e a primeira seção — como seção de caminho vazio.
    ///
    /// <para>
    /// Os dois pontos são defeitos corrigidos: o fragmentador anterior só
    /// reconhecia <c>##</c> (documento com <c>#</c> + <c>###</c> dava zero
    /// fragmentos) e descartava o preâmbulo (4 parágrafos perdidos no corpus de
    /// `0c`, e a cobertura do próprio corpus de `0b` ficava em 93,5%).
    /// </para>
    /// </summary>
    private static (string? Title, List<Section> Sections) ParseSections(string text)
    {
        string? documentTitle = null;
        var sections = new List<Section>();
        var stack = new List<(int Level, string Title)>();
        var body = new List<string>();

        void Close()
        {
            var content = string.Join('\n', body).Trim();
            if (content.Length > 0)
            {
                sections.Add(new Section([.. stack.Select(entry => entry.Title)], content));
            }

            body.Clear();
        }

        foreach (var line in text.Split('\n'))
        {
            var match = HeadingPattern.Match(line);
            if (!match.Success || match.Index != 0 || match.Length != line.Length)
            {
                body.Add(line);
                continue;
            }

            var level = match.Groups[1].Value.Length;
            var title = match.Groups[2].Value;

            if (level == 1 && documentTitle is null && stack.Count == 0)
            {
                Close();
                documentTitle = title;
                continue;
            }

            Close();
            while (stack.Count > 0 && stack[^1].Level >= level)
            {
                stack.RemoveAt(stack.Count - 1);
            }

            stack.Add((level, title));
        }

        Close();
        return (documentTitle, sections);
    }
}
