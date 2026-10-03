namespace Buteco.Connectors.Connectors;

/// <summary>
/// Segundo contrato de conector: listar a raiz de uma pasta e entregar o markdown de
/// um arquivo. Registrado keyed, com a chave do provedor.
/// </summary>
/// <remarks>
/// <para>
/// <b>Consumidor esperado: o ciclo de sincronização da #105.</b> Nesta change só os
/// testes o chamam (design.md, Risks: convenção 25). Se a #105 for descartada, este
/// contrato sai.
/// </para>
/// <para>
/// A listagem é completa ou falha: uma página que falhe faz a operação inteira falhar,
/// porque a #105 exclui documento por ausência na listagem.
/// </para>
/// </remarks>
public interface IFolderContentSource
{
    Task<RootListing> ListRootAsync(string folderId, CancellationToken cancellationToken);

    /// <summary>O markdown de um arquivo suportado, como veio da listagem.</summary>
    Task<string> GetMarkdownAsync(RootFile file, CancellationToken cancellationToken);
}
