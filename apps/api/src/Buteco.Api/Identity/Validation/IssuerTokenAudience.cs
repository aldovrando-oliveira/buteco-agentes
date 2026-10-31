namespace Buteco.Api.Identity.Validation;

// aud que o token do emissor precisa trazer para valer nas rotas do apps/api (design.md da
// change usuarios-locais-login-emissor, D3.3). Constante, e não configuração: é contrato
// entre o emissor e este app, igual em todo ambiente, como "buteco-inbox" no apps/inbox e
// "buteco-connectors" no apps/connectors (#208 D5).
public static class IssuerTokenAudience
{
    public const string Value = "buteco-api";
}
