namespace Buteco.Api.KnowledgeDocuments.Options;

/// <summary>
/// Teto de tamanho por documento. Constante de produto, **não configurável**
/// (convenção 2: opção só existe quando há cenário real de alguém precisar de
/// outro valor).
/// </summary>
/// <remarks>
/// O número não vem do framework. O default do Kestrel é 30.000.000 bytes
/// (28,6 MiB) e o repositório não configura nada, então é ele que vale hoje —
/// mas ~355 arquivos de 80 KB caberiam num único corpo, o que não é o limite
/// que importa. 1 MiB é ~12× o maior documento real observado (79,2 KB) e
/// mantém previsível o custo de embedding de um documento na etapa de
/// indexação.
///
/// Medido em **bytes UTF-8**, não caracteres, e sobre o texto **já extraído**
/// (design.md, D5): é a mesma string que a coluna gerada
/// <c>ContentLengthBytes</c> mede, então a interface consegue mostrar quanto
/// falta para o teto sem que os dois números divirjam.
/// </remarks>
public static class KnowledgeDocumentLimits
{
    public const int MaxContentBytes = 1024 * 1024;
}
