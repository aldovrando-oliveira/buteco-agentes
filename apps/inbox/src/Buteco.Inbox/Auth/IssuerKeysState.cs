namespace Buteco.Inbox.Auth;

// Duplicado deliberadamente de
// apps/connectors/src/Buteco.Connectors/Auth/IssuerKeysState.cs
// — sem libs/ (design.md da change validacao-jwks-inbox-connectors, D10). Qualquer
// mudança aqui precisa ser espelhada lá, e vice-versa; o que difere de propósito é a
// audience e a tabela de clientes.

// Se alguma busca de chaves do emissor já deu certo neste processo (change
// validacao-jwks-inbox-connectors, D4). Enquanto não, o desafio do token do emissor
// vira 503: sem chave, "não sei validar ainda" e "token inválido" seriam o mesmo 401
// (F5), e um 401 faria o painel deslogar por uma queda passageira do apps/api.
// Nunca volta a falso: depois de obtidas, o ConfigurationManager mantém as últimas
// chaves boas (F2).
public sealed class IssuerKeysState
{
    private volatile bool _obtained;

    public bool Obtained => _obtained;

    public void MarkObtained() => _obtained = true;
}
