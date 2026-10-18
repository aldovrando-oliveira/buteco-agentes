using Microsoft.Extensions.Options;

namespace Buteco.Inbox.Orchestration;

/// <summary>
/// Checagem de boot de <c>Debounce:SessionSerializationMaxWait</c> (change
/// inbox-serializacao-por-sessao, D3; convenção 8). Chamada do <c>Program.cs</c> sobre o
/// host construído, no idioma de <c>ValidateRouteAuthenticationClassification</c>.
///
/// <para>
/// <b>Os dois casos que reprovam desligariam a serialização em silêncio</b>, e isso é o
/// defeito da #162 de volta:
/// </para>
/// <list type="bullet">
/// <item>teto menor ou igual a zero: nenhuma linha em <c>Dispatching</c> bloquearia;</item>
/// <item>teto menor ou igual a <c>Debounce:Window</c>: o claim só acontece com
/// <c>LastMessageAt</c> mais velho que a janela, então toda linha em <c>Dispatching</c>
/// já nasceria além do teto.</item>
/// </list>
///
/// <para>
/// <b>Por que falhar e não corrigir:</b> um valor trocado em silêncio faria o processo
/// rodar um teto que ninguém escolheu.
/// </para>
///
/// <para>
/// <b>O registro no <c>Program.cs</c> é provado por teste:</b> os testes de
/// <c>apps/inbox</c> sobem a aplicação real pela <c>WebApplicationFactory</c>, e
/// <c>DebounceOptionsValidationTests.InvalidMaxWait_FailsTheRealApplicationStartup</c>
/// reprova se a chamada sair de lá.
/// </para>
/// </summary>
public static class DebounceOptionsValidation
{
    public static void ValidateDebounceSessionSerializationMaxWait(this IHost host)
    {
        var options = host.Services.GetRequiredService<IOptions<DebounceOptions>>().Value;
        var maxWait = options.SessionSerializationMaxWait;

        if (maxWait <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"Debounce:SessionSerializationMaxWait está configurado como {maxWait}, e precisa ser maior que zero: "
              + "é por quanto tempo um disparo em andamento segura a mensagem seguinte da mesma conversa. "
              + "Com zero ou menos, a serialização por sessão fica desligada sem aviso. "
              + "O valor não é corrigido automaticamente. Corrija a configuração (padrão: 00:10:00).");
        }

        if (maxWait <= options.Window)
        {
            throw new InvalidOperationException(
                $"Debounce:SessionSerializationMaxWait está configurado como {maxWait}, e precisa ser maior que "
              + $"Debounce:Window ({options.Window}): o disparo só acontece depois da janela, então com um teto "
              + "que não a excede todo disparo em andamento já nasceria vencido, e a serialização por sessão "
              + "ficaria desligada sem aviso. O valor não é corrigido automaticamente. "
              + "Corrija a configuração (padrão: 00:10:00).");
        }

        // Linha de sucesso: sem ela, uma checagem que passa é indistinguível de uma
        // checagem que não rodou (mesmo idioma de EmbeddingBatchSizeValidation, apps/workers).
        host.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(DebounceOptionsValidation).FullName!)
            .LogInformation(
                "Teto da serialização por sessão conferido: {SessionSerializationMaxWait} (Debounce:Window {Window}).",
                maxWait,
                options.Window);
    }
}
