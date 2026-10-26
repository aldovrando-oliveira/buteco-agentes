namespace Buteco.Connectors.Auth;

// Duplicado deliberadamente de
// apps/inbox/src/Buteco.Inbox/Auth/IssuerTokenAudience.cs
// — sem libs/ (design.md da change validacao-jwks-inbox-connectors, D10). Qualquer
// mudança aqui precisa ser espelhada lá, e vice-versa; o que difere de propósito é a
// audience e a tabela de clientes.

// aud que o token do emissor precisa trazer para valer aqui (change
// validacao-jwks-inbox-connectors, D5). Constante, e não configuração: é contrato
// entre o emissor e este app, igual em todo ambiente. O apps/inbox exige
// "buteco-inbox", e o apps/api (#180) "buteco-api".
public static class IssuerTokenAudience
{
    public const string Value = "buteco-connectors";
}
