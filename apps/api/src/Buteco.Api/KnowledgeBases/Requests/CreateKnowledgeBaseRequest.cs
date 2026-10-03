namespace Buteco.Api.KnowledgeBases.Requests;

/// <summary>
/// <c>ContentMode</c> é string, e não o enum, para que valor desconhecido vire
/// <c>ValidationProblem</c> em <c>contentMode</c>, no mesmo formato das outras
/// recusas, em vez do 400 genérico de desserialização (design.md da change
/// catalogo-base-sincronizada, D11).
///
/// <para>
/// <c>Provider</c> e <c>FolderId</c> são obrigatórios em <c>Synced</c> e proibidos em
/// <c>Manual</c> (design.md da change criacao-base-sincronizada, D6). <c>null</c> conta
/// como ausente: o <c>System.Text.Json</c> não distingue propriedade ausente de
/// propriedade nula num <c>record</c> com <c>string?</c>. Nome e URL da pasta NÃO
/// entram aqui: vêm do <c>apps/connectors</c>, e os do corpo são ignorados (D7).
/// </para>
/// </summary>
public record CreateKnowledgeBaseRequest(
    string? Name,
    string? Description,
    string? ContentMode = null,
    string? Provider = null,
    string? FolderId = null);
