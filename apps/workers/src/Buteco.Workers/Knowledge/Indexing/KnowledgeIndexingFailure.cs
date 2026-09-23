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
    /// <summary>
    /// <b>DEFEITO CONHECIDO, REGISTRADO E NÃO CORRIGIDO AQUI.</b> Os três braços
    /// de <see cref="HttpRequestException"/> com status — <c>429</c>, <c>401</c>
    /// e <c>403</c> — são <b>inalcançáveis no caminho real</b>: o único provedor
    /// de embedding implementado é o <c>openai</c>, e o SDK dele
    /// (<c>System.ClientModel</c>) lança <c>ClientResultException</c>, que deriva
    /// de <see cref="Exception"/> e <b>não</b> de <see cref="HttpRequestException"/>
    /// — verificado por execução na change <c>metricas-embedding-coleta</c>.
    ///
    /// <para>
    /// Consequência: <b>todo</b> erro HTTP do gateway cai no braço genérico do
    /// fim, inclusive o <c>502 upstream_error</c> que motivou a
    /// <c>indexacao-lote-de-fragmentos</c>. O operador lê *"erro interno …
    /// Reindexe o documento"* num caso em que reindexar não resolve.
    /// </para>
    ///
    /// <para>
    /// <b>Não foi corrigido nesta change porque corrigi-lo muda texto de
    /// tela</b>, que é comportamento observável e não cabe numa change cujo
    /// objeto é coleta. O assunto está no <c>02-HISTORICO_E_STATUS.md</c>, em
    /// *Itens em aberto* → *Abertos por <c>indexacao-lote-de-fragmentos</c>*, no
    /// item do texto de falha da tela de documentos — <b>mesmo arquivo, mesmo
    /// braço, duas causas</b>. Quem corrigir faz as duas coisas: acrescenta o
    /// braço de <c>ClientResultException</c> <b>antes</b> dos de
    /// <see cref="HttpRequestException"/> e reescreve o texto genérico. Corrigir
    /// metade deixa o outro defeito de pé.
    /// </para>
    ///
    /// <para>
    /// A <b>classificação de M30</b> não depende disto: ela grava a
    /// <c>FailurePhase</c> de <c>EmbeddingMetricsValues</c>, determinada por onde
    /// o código estava, e o status HTTP fica na linha de <c>embedding_calls</c>.
    /// </para>
    /// </summary>
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
