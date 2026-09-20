namespace Buteco.Workers.Options;

/// <summary>
/// Intervalo da varredura de tasks não-terminais envelhecidas
/// (<c>Diagnostics/NonTerminalTaskDetectorService</c>).
/// </summary>
/// <remarks>
/// <b>A JANELA NÃO ESTÁ AQUI, E É DE PROPÓSITO.</b> Ela é lida em runtime de
/// <see cref="AgentDelegationToolOptions.Timeout"/> — é o timeout de espera da
/// delegação, não um valor independente (design.md da change
/// delegacao-diagnostico, D3). Assim uma task reportada já sobreviveu a uma
/// espera de delegação inteira, e quem mudar aquele timeout move a janela junto,
/// sem recalibração manual: é o único desenho em que a referência não consegue
/// envelhecer separada do estado que a mediu (convenção 22).
///
/// <para>
/// <b>O INTERVALO NÃO TEM BASE MEDIDA, e dizê-lo é o que a convenção 13 pede
/// quando não há base.</b> É escolha de <b>resolução</b>, não limiar de
/// correção: 30 s é um quarto da janela, então um episódio que dure uma janela
/// inteira produz ~4 observações — o suficiente para separar um pico de uma
/// amostra isolada, que é a única coisa que a série precisa fazer.
/// <b>Recalibrar é tarefa de quem ler a série pela primeira vez</b> (a change
/// `replicas-de-worker`), não descoberta de quem vier depois.
/// </para>
///
/// <para>
/// <b>Foi RECUSADO ancorar em <c>DebounceOptions.SweepInterval</c> (2 s,
/// apps/inbox)</b>, que é o único intervalo de varredura periódica já escolhido
/// nesta base. Aquele número foi escolhido para <b>debounce de mensagem de
/// contato</b>: usá-lo aqui seria citar um número correto respondendo a outra
/// pergunta — a ocorrência 3 da convenção 22, em que o sistema não muda e muda a
/// pergunta feita ao número.
/// </para>
///
/// <para>
/// Não é vinculado a nenhuma seção de configuração em <c>Program.cs</c>, mesmo
/// espírito de <see cref="AgentDelegationToolOptions"/>: o default é constante
/// de produto, e existir como
/// <see cref="Microsoft.Extensions.Options.IOptions{TOptions}"/> serve para o
/// guarda encurtar o intervalo sem pagar 30 s por teste — não porque haja
/// cenário real de alguém precisar de outro valor em produção (convenção 2).
/// </para>
/// </remarks>
public sealed class TaskDiagnosticsOptions
{
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(30);
}
