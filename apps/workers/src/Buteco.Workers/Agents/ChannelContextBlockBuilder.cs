namespace Buteco.Workers.Agents;

/// <summary>
/// Monta o bloco de contexto de canal concatenado às <c>Instructions</c> do
/// agente a cada execução — nunca persistido, nunca parte do histórico de
/// conversa (design.md da change inbox-contexto-canal, D5). Função pura,
/// arquivo separado de <see cref="TemporalContextBlockBuilder"/> de propósito
/// (D5) — os dois blocos não precisam compartilhar infraestrutura para
/// funcionar, e mantê-los separados evita acoplamento acidental entre eles.
/// </summary>
/// <remarks>
/// Diferente de <c>Contact.DisplayName</c> (fora do prompt por decisão, D1),
/// os dois campos aqui (<paramref name="channelType"/> via parâmetro de
/// <see cref="Build"/>, <paramref name="contactExternalId"/> idem) são
/// sempre atribuídos pelo adapter/provedor do canal — nunca texto digitado
/// pelo usuário final. Por isso o marcador abaixo não precisa resistir a
/// conteúdo adversarial (D5): não há conteúdo adversarial para ele
/// delimitar.
/// </remarks>
public static class ChannelContextBlockBuilder
{
    private const string NotAUserMessageMarker =
        "[Contexto de canal — não é uma mensagem do usuário, não responda a ele diretamente]";

    /// <summary>
    /// Retorna <c>null</c> quando os dois campos estão ausentes (D5/D6, caso
    /// 1) — nenhum bloco a concatenar. Com só um campo disponível, o bloco
    /// mostra só a linha correspondente, sem mencionar o campo ausente como
    /// desconhecido ou com placeholder (D6, caso 2; convenção 13). Nomeia
    /// <paramref name="contactExternalId"/> pelo que ele é — identificador
    /// atribuído pelo provedor do canal — nunca como telefone (D4).
    /// </summary>
    public static string? Build(string? channelType, string? contactExternalId)
    {
        if (string.IsNullOrEmpty(channelType) && string.IsNullOrEmpty(contactExternalId))
        {
            return null;
        }

        var lines = new List<string> { NotAUserMessageMarker };

        if (!string.IsNullOrEmpty(channelType))
        {
            lines.Add($"Canal de origem desta conversa: {channelType}");
        }

        if (!string.IsNullOrEmpty(contactExternalId))
        {
            lines.Add($"Identificador do contato atribuído pelo canal: {contactExternalId}");
        }

        return string.Join('\n', lines);
    }
}
