using System.Text.Json.Serialization;

namespace Buteco.Api.KnowledgeBases.Entities;

/// <summary>
/// De onde vêm os documentos de uma <see cref="KnowledgeBase"/> (design.md da
/// change catalogo-base-sincronizada).
/// </summary>
/// <remarks>
/// Conjunto <b>fechado</b>, ao contrário do provedor, que é string aberta: o tipo
/// tem comportamento no <c>apps/api</c> (quem pode escrever documento), e o
/// provedor pertence ao app que sincroniza.
///
/// <b>Imutável</b> depois do cadastro. Faz parte da chave alternativa
/// <c>(Id, ContentMode)</c> que a FK composta do documento referencia (D4), e o
/// change tracker se recusa a modificar propriedade de chave.
///
/// String no fio e no banco, nunca inteiro ordinal (convenção 12).
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<KnowledgeBaseContentMode>))]
public enum KnowledgeBaseContentMode
{
    Manual,
    Synced,
}
