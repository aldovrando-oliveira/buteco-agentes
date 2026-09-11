namespace Buteco.Workers.Knowledge.Chunking;

/// <summary>
/// Fragmento produzido pelo fragmentador, antes de virar linha no banco.
/// </summary>
/// <param name="Ordinal">Posição no documento, a partir de zero.</param>
/// <param name="Text">
/// Texto <b>emitido</b> — já com o prefixo de caminho de cabeçalhos. É este
/// texto que vai ao provedor de embedding e é este que é gravado, para que o
/// vetor e o texto nunca divirjam.
/// </param>
/// <param name="HeadingPath">
/// Caminho de cabeçalhos que originou o fragmento. Não é persistido: existe
/// para diagnóstico e para os testes poderem afirmar a fronteira sem depender
/// do formato do prefixo.
/// </param>
public sealed record ChunkedFragment(int Ordinal, string Text, IReadOnlyList<string> HeadingPath);
