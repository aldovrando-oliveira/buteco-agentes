namespace Buteco.Connectors.Connectors;

public enum FolderEntryKind
{
    SharedDrive,
    Folder,
}

/// <summary>Um item da navegação: Drive Compartilhado ou pasta.</summary>
public sealed record FolderEntry(string Id, string Name, FolderEntryKind Kind, string WebUrl);

/// <summary>A pasta descrita, depois de verificado o acesso.</summary>
public sealed record FolderDescription(string Id, string Name, string WebUrl);

/// <summary>
/// Arquivo suportado da raiz. <see cref="ExternalVersion"/> é o marcador opaco do
/// provedor (D3 da change catalogo-base-sincronizada). <see cref="MimeType"/> é o que
/// o provedor informou, usado pelo próprio conector para escolher como entregar.
/// </summary>
public sealed record RootFile(string ExternalRef, string Name, string ExternalVersion, string MimeType);

/// <summary>Arquivo da raiz que não entra na base, com o motivo como código.</summary>
public sealed record IgnoredFile(string ExternalRef, string Name, string Code, string? Detail);

public sealed record RootListing(IReadOnlyList<RootFile> Files, IReadOnlyList<IgnoredFile> Ignored);
