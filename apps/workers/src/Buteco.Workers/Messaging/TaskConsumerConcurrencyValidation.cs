using Buteco.Workers.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Buteco.Workers.Messaging;

/// <summary>
/// Checagem de boot de <c>TaskConsumer:MaxConcurrentExecutions</c>. Convenção 8, no
/// idioma de <c>ValidateEmbeddingBatchSize</c>: chamada do <c>Program.cs</c> sobre o
/// host construído, mensagem escrita para quem opera (change
/// workers-consumo-concorrente, D3).
///
/// <para>
/// <b>Por que 0 reprova, e não vira 1:</b> <c>prefetchCount: 0</c> no RabbitMQ quer
/// dizer <b>sem limite</b>, e <c>consumerDispatchConcurrency: 0</c> é inválido. O valor
/// mais fácil de digitar errado é o que mais quebra.
/// </para>
///
/// <para>
/// <b>Por que falhar e não corrigir:</b> o mesmo de <c>ValidateEmbeddingBatchSize</c>.
/// Este valor existe para ser ajustado à mão (calibração na #48, rollback para 1), e
/// um valor trocado em silêncio faria o processo rodar um número que ninguém pediu.
/// </para>
///
/// <para>
/// <b>O REGISTRO da chamada não é provado por nenhum teste:</b> os testes de
/// apps/workers montam o host à mão. Remover a linha do <c>Program.cs</c> deixa
/// <c>TaskConsumerConcurrencyValidationTests</c> verde e a produção sem checagem — é
/// conferência manual de escopo.
/// </para>
/// </summary>
public static class TaskConsumerConcurrencyValidation
{
    public static void ValidateTaskConsumerConcurrency(this IHost host)
    {
        using var scope = host.Services.CreateScope();
        var declared = scope.ServiceProvider.GetRequiredService<IOptions<TaskConsumerOptions>>().Value;

        if (declared.MaxConcurrentExecutions is >= TaskConsumerOptions.MinMaxConcurrentExecutions
            and <= TaskConsumerOptions.MaxMaxConcurrentExecutions)
        {
            // Linha de sucesso com o valor: sem ela, uma checagem que passa é
            // indistinguível de uma que não rodou, e saber qual N estava em vigor é
            // metade de ler as séries de tempo de fila e de lock (D9).
            scope.ServiceProvider
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(TaskConsumerConcurrencyValidation))
                .LogInformation(
                    "Execuções simultâneas por instância do consumidor de tasks conferidas: {MaxConcurrentExecutions}.",
                    declared.MaxConcurrentExecutions);

            return;
        }

        throw new InvalidOperationException(
            $"TaskConsumer:MaxConcurrentExecutions está configurado como {declared.MaxConcurrentExecutions}, e precisa "
          + $"estar em [{TaskConsumerOptions.MinMaxConcurrentExecutions}, {TaskConsumerOptions.MaxMaxConcurrentExecutions}]: "
          + "é quantas tasks esta instância executa ao mesmo tempo, e também o prefetch do canal de tasks. "
          + "0 reprova porque prefetch 0 no RabbitMQ quer dizer sem limite. O teto vem do pool de conexões do "
          + "Postgres deste processo (Maximum Pool Size 100 do Npgsql): cada execução segura uma conexão dedicada "
          + "ao lock de contexto e mais uma de vida curta. O valor não é corrigido automaticamente. "
          + "Corrija a configuração (padrão: 4; rollback: 1).");
    }
}
