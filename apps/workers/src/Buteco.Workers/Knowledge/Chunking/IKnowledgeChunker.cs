namespace Buteco.Workers.Knowledge.Chunking;

/// <summary>
/// Fragmenta o conteúdo de um documento.
///
/// <para>
/// <b>A interface existe para ser substituível em teste</b>, pelo mesmo motivo
/// que <c>IChatClientResolver</c> existe: a guarda de "sucesso com zero
/// fragmentos é recusado" (spec própria) só é verificável se der para colocar no
/// lugar um fragmentador que devolve conjunto vazio para documento com
/// conteúdo. Sem a interface, essa guarda seria código sem gatilho — e guarda
/// que não se vê reprovar não vale (convenção 15).
/// </para>
/// </summary>
public interface IKnowledgeChunker
{
    IReadOnlyList<ChunkedFragment> Chunk(string extractedText);
}
