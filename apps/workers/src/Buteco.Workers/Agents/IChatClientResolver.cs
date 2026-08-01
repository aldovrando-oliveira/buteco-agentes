using Microsoft.Extensions.AI;

namespace Buteco.Workers.Agents;

/// <summary>
/// Resolve o <see cref="IChatClient"/> correto por provedor, construído por
/// chamada (sem cache entre execuções — ver design.md da change
/// backend-multi-provedor-llm, Decision 7). Interface existe especificamente
/// para ser testável isolando "qual tipo de client é construído para qual
/// provedor" sem chamada de rede real.
/// </summary>
public interface IChatClientResolver
{
    IChatClient Resolve(string provider, string model);
}
