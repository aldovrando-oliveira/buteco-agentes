namespace Buteco.Workers.Agents.Entities;

/// <summary>
/// Espelho da entidade de mesmo nome de <c>apps/api</c>: um par <c>(Provider, Model)</c>
/// habilitado na lista curada (tabela <c>llm_enabled_models</c>), lido pelo
/// <c>AgentDelegationToolSetResolver</c> para recusar a delegação de Source ou Target com
/// modelo não habilitado (design.md da change modelos-curados-por-provedor, D8). Só
/// <c>apps/api</c> escreve nesta tabela em produção; o construtor público existe para as
/// fixtures de teste.
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
