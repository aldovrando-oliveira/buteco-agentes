using Microsoft.Extensions.AI;

namespace Buteco.Workers.Agents;

/// <summary>
/// Resolve o <see cref="IChatClient"/> correto por provedor, reutilizando UMA
/// instância por par <c>(provider, model)</c> durante toda a vida do processo.
///
/// <para>
/// A change <c>backend-multi-provedor-llm</c> construía por chamada, e registrou
/// o gatilho para trocar: <i>"simples de trocar por cache depois, se perfilamento
/// mostrar necessidade"</i> (Decision 7 do design.md arquivado). O perfilamento
/// mostrou — cada mensagem vazava um pool de conexões HTTP —, e
/// <c>fix-vazamento-httpclient-chat</c> disparou esse gatilho. Não é decisão
/// contrariada: é a condição que ela própria escreveu, acontecendo.
/// </para>
///
/// <para>
/// Interface existe especificamente para ser testável isolando "qual tipo de
/// client é construído para qual provedor" sem chamada de rede real.
/// </para>
/// </summary>
public interface IChatClientResolver
{
    IChatClient Resolve(string provider, string model);
}
