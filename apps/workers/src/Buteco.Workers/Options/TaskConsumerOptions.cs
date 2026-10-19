namespace Buteco.Workers.Options;

/// <summary>
/// Quantas tasks uma instância de apps/workers executa ao mesmo tempo
/// (<c>Messaging/TaskJobConsumer</c>; change workers-consumo-concorrente, #163).
/// </summary>
/// <remarks>
/// <para>
/// <b>UM VALOR, DOIS LUGARES DO CANAL DE TASKS, E SÓ DELE</b> (design.md, D1): o
/// <c>prefetchCount</c> do <c>BasicQos</c> e o <c>consumerDispatchConcurrency</c> do
/// <c>CreateChannelOptions</c>. Foi recusado ter duas opções: prefetch maior que a
/// concorrência prende entregas no processo, fora do alcance de outras instâncias,
/// e reabre o autodeadlock da delegação sem erro visível. Não mora em
/// <see cref="RabbitMqOptions"/> porque aquela serve também ao publicador e ao
/// consumidor de indexação, que não devem ler este valor.
/// </para>
/// <para>
/// <b>O DEFAULT 4 NÃO É MEDIDO</b> (D2, convenção 13). É escolha de forma: uma
/// instância atende uma cadeia de delegação de profundidade 3 (Source + 3 Targets
/// esperando) ou duas delegações de profundidade 1 ao mesmo tempo. O <c>C</c> de pico
/// (conversas delegando ao mesmo tempo) não foi medido. <b>Gatilho de
/// recalibração:</b> o <c>C</c> de pico observado pela coluna de <c>Submitted</c> do
/// <c>NonTerminalTaskDetector</c>. <b>Posição:</b> a #48.
/// </para>
/// <para>
/// <b>O TETO DE 32 PROTEGE O POOL DO NPGSQL DESTE PROCESSO</b> (D3). Cada execução
/// segura uma conexão dedicada ao lock de contexto (<c>ConversationContextLock</c>) e,
/// enquanto isso, no máximo uma de vida curta (tool calls seriais; tarefa 1.3 e
/// #175): 32 × 2 = 64, mais o consumidor de indexação, o detector e as checagens de
/// boot, contra o <c>Maximum Pool Size</c> default de 100. <b>Não protege o
/// servidor:</b> o <c>max_connections</c> do Postgres é dividido com apps/api e
/// apps/inbox, e subir <c>N</c> acima do valor verificado antes do deploy exige
/// refazer aquela verificação (<c>docs/configuration.md</c>).
/// </para>
/// <para>
/// <b>ROLLBACK: <c>1</c>.</b> Prefetch 1 e concorrência 1 são exatamente o estado
/// anterior a esta opção (o construtor de <c>CreateChannelOptions</c> já fixava 1), e
/// é configuração, sem imagem nova (D4).
/// </para>
/// </remarks>
public sealed class TaskConsumerOptions
{
    public const string SectionName = "TaskConsumer";

    public const int MinMaxConcurrentExecutions = 1;

    public const int MaxMaxConcurrentExecutions = 32;

    public int MaxConcurrentExecutions { get; set; } = 4;
}
