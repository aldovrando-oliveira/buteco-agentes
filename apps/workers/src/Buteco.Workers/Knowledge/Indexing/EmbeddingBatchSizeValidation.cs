using Buteco.Workers.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Buteco.Workers.Knowledge.Indexing;

/// <summary>
/// Checagem de boot do tamanho de lote de embedding. Convenção 8, no idioma das
/// outras três (<c>ValidateTimeZoneConfiguration</c>,
/// <c>ValidateEmbeddingIndexConsistency</c>,
/// <c>ValidateKnowledgeExtractorRegistrations</c>): chamada do
/// <c>Program.cs</c> sobre o host construído, mensagem escrita para quem opera.
///
/// <para>
/// <b>Por que este parâmetro ganha checagem e os outros de indexação não:</b>
/// <c>Embedding:BatchSize</c> é o primeiro desta base <b>desenhado para ser
/// variado à mão em produção</b> — o operador vai baixá-lo e subi-lo procurando
/// o teto do gateway, que não foi estabelecido. O modo de errar é digitar
/// <c>0</c>.
/// </para>
///
/// <para>
/// <b>Por que no boot e não na primeira indexação:</b> sem esta checagem, um
/// valor inválido só apareceria dentro de <c>IndexAsync</c>, por documento —
/// queimando as três tentativas de cada um e gravando <c>Failed</c> com motivo
/// técnico em toda a base. É o mesmo defeito que <c>docs/configuration.md</c>
/// nomeia para <c>EMBEDDING_MODEL</c>/<c>EMBEDDING_DIMENSIONS</c>: *"a ausência
/// não impede o boot e só aparece na primeira indexação"*.
/// </para>
///
/// <para>
/// <b>Por que falhar e não clampar para um mínimo.</b> Corrigir em silêncio faria
/// o processo rodar um valor que o operador não pediu — e a medição seguinte
/// seria feita sobre um número que ninguém escolheu. Numa mudança cujo propósito
/// é <b>medir variando o parâmetro</b>, clampar é o defeito, não a proteção.
/// </para>
///
/// <para>
/// <b>Esta extensão não é provada por nenhum teste da suíte no que diz respeito
/// ao REGISTRO da chamada:</b> os testes de <c>apps/workers</c> montam o host à
/// mão, não pelo <c>Program.cs</c>. Remover a linha de lá deixa
/// <c>EmbeddingBatchSizeValidationTests</c> verde e a produção sem checagem — é
/// conferência manual de escopo, como o próprio <c>Program.cs</c> já registra
/// para o <c>NonTerminalTaskDetectorService</c>.
/// </para>
/// </summary>
public static class EmbeddingBatchSizeValidation
{
    public static void ValidateEmbeddingBatchSize(this IHost host)
    {
        using var scope = host.Services.CreateScope();
        var declared = scope.ServiceProvider.GetRequiredService<IOptions<EmbeddingOptions>>().Value;

        if (declared.BatchSize > 0)
        {
            // LINHA DE SUCESSO, e não enfeite (change compactacao-historico, D6):
            // sem ela, uma checagem que passa é indistinguível de uma checagem que
            // não rodou — a mesma ambiguidade que a linha de início do
            // NonTerminalTaskDetectorService existe para não ter. O valor vai na
            // linha porque este parâmetro é feito para ser VARIADO À MÃO em
            // produção: saber qual estava em vigor é metade do diagnóstico.
            scope.ServiceProvider
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(EmbeddingBatchSizeValidation))
                .LogInformation("Tamanho de lote de embedding conferido: {BatchSize}.", declared.BatchSize);

            return;
        }

        // Nomeia o valor ENCONTRADO e o que se espera — idioma das outras
        // checagens desta base, e o que permite corrigir sem adivinhar.
        throw new InvalidOperationException(
            $"Embedding:BatchSize está configurado como {declared.BatchSize}, e precisa ser maior que zero: "
          + "é quantos fragmentos vão em cada chamada ao gerador de embedding. "
          + "O valor não é corrigido automaticamente, porque este parâmetro existe para ser ajustado "
          + "à mão — ajustá-lo é como o teto do gateway é descoberto, e um valor trocado em silêncio "
          + "invalidaria a medição. Corrija a configuração (padrão: 250).");
    }
}
