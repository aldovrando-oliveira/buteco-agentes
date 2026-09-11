using System.Net.Sockets;

namespace Buteco.Workers.Knowledge.Indexing;

/// <summary>
/// Traduz a causa de uma falha em texto <b>de operador</b>.
///
/// <para>
/// A tela de documentos mostra este texto <b>completo, sem truncar</b> — é a
/// única cópia de falha que ela tem. Por isso ele descreve o que falhou e o que
/// fazer, e NUNCA carrega nome de tipo de exceção, pilha de chamadas ou mensagem
/// de biblioteca repassada sem tradução: nada disso ajuda quem opera, e expor
/// pilha numa tela é também superfície de informação interna.
/// </para>
/// </summary>
public static class KnowledgeIndexingFailure
{
    public static string Describe(Exception exception) => exception switch
    {
        InvalidOperationException invalid when invalid.Message.Contains("dimensão", StringComparison.OrdinalIgnoreCase)
            => invalid.Message,

        InvalidOperationException invalid when invalid.Message.Contains("não está configurado", StringComparison.OrdinalIgnoreCase)
            => "O provedor de embedding não está configurado neste ambiente. "
             + "A indexação não pode ser feita até que a configuração seja corrigida.",

        InvalidOperationException invalid when invalid.Message.Contains("não é suportado", StringComparison.OrdinalIgnoreCase)
            => "O provedor de embedding configurado não é suportado para indexação. "
             + "Verifique a configuração do ambiente.",

        HttpRequestException http when http.StatusCode == System.Net.HttpStatusCode.TooManyRequests
            => "O serviço de embedding recusou as requisições por limite de uso (HTTP 429) "
             + "e as tentativas se esgotaram. Reindexe o documento mais tarde; "
             + "se persistir, o limite do serviço precisa ser revisto.",

        HttpRequestException http when http.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
            => "O serviço de embedding recusou a credencial configurada. "
             + "A indexação não pode ser feita até que a credencial seja corrigida.",

        HttpRequestException
            => "Não foi possível falar com o serviço de embedding e as tentativas se esgotaram. "
             + "Reindexe o documento quando o serviço estiver disponível.",

        SocketException or TimeoutException or TaskCanceledException
            => "O serviço de embedding não respondeu no tempo esperado e as tentativas se esgotaram. "
             + "Reindexe o documento mais tarde.",

        // Rede de segurança. NÃO repassa exception.Message: mensagem de
        // biblioteca é texto de desenvolvedor, e vazaria detalhe interno para a
        // tela do operador. O diagnóstico técnico sai por log, que é onde ele
        // serve.
        _ => "A indexação falhou por um erro interno e as tentativas se esgotaram. "
           + "Reindexe o documento; se persistir, é caso de suporte técnico.",
    };

    public const string EmptyFragmentSet =
        "A fragmentação do documento não produziu nenhum trecho indexável. "
      + "Verifique se o conteúdo enviado não está vazio ou contém apenas formatação.";
}
