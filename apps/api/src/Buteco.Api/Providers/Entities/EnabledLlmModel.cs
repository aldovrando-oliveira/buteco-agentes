namespace Buteco.Api.Providers.Entities;

/// <summary>
/// Um par <c>(Provider, Model)</c> habilitado na lista curada de modelos (tabela
/// <c>llm_enabled_models</c>; design.md da change modelos-curados-por-provedor, D1).
///
/// <para>
/// <b>O par é a identidade</b>, e é o mesmo par que o agente grava em
/// <c>agents."Provider"</c>/<c>agents."Model"</c>. Sem <c>Id</c>, sem data e sem autor:
/// nenhuma rota nem tela os pede (convenção 2). Sem FK de <c>agents</c> para cá, de
/// propósito: retirar um modelo não é bloqueado, e o agente fica com o par gravado,
/// recusando tarefas até ser editado (decisão do dono de 19/11/2026).
/// </para>
///
/// <para>
/// O <c>apps/workers</c> lê a mesma tabela pela entidade espelho de mesmo nome; o acordo
/// entre os dois contextos é o de <c>tests/CrossAppTaskStoreCompatibility.Tests</c> (D9).
/// </para>
/// </summary>
public sealed class EnabledLlmModel
{
    private EnabledLlmModel()
    {
    }

    public EnabledLlmModel(string provider, string model)
    {
        Provider = provider;
        Model = model;
    }

    public string Provider { get; private set; } = null!;

    public string Model { get; private set; } = null!;
}
