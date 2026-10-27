namespace Buteco.Api.Identity.Storage;

/// <summary>
/// As constantes da limpeza de <c>openiddict_tokens</c> e <c>openiddict_authorizations</c>
/// (design.md da change limpeza-tokens-openiddict, D3 e D5). Constantes no código, e não
/// opção (convenção 2): nenhum cenário pede outro valor.
/// </summary>
public static class IdentityTokenPrunePolicy
{
    /// <summary>
    /// Idade mínima de uma linha para a limpeza poder removê-la. Precisa ser ≥ duração do
    /// refresh token (14 dias, o padrão do OpenIddict): assim a linha de um refresh token
    /// resgatado só sai quando o token já estaria vencido, e a detecção de reuso vale pelo
    /// prazo inteiro dele. O boot afirma isso (<c>ValidateIdentityIssuerConfiguration</c>).
    /// É também o padrão do <c>OpenIddict.Quartz</c>, pelo mesmo motivo.
    /// </summary>
    public static readonly TimeSpan MinimumAge = TimeSpan.FromDays(14);

    /// <summary>
    /// Intervalo entre dois ciclos. O mesmo do <c>OpenIddict.Quartz</c> e da verificação
    /// de rotação das chaves.
    /// </summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// <summary>
    /// Chave do <c>pg_try_advisory_xact_lock</c> que deixa uma instância por ciclo. São os
    /// bytes ASCII de <c>BUTECOP1</c> lidos como inteiro big-endian, na forma de uma chave
    /// <c>bigint</c>, pelo mesmo motivo da <c>IdentityKeyRotationPolicy.AdvisoryLockKey</c>
    /// (<c>BUTECOK1</c>): o espaço de 64 bits não se sobrepõe ao de dois <c>int4</c> do
    /// <c>apps/workers</c>. Diferente da chave da rotação, para uma não esperar pela outra.
    /// </summary>
    public const long AdvisoryLockKey = 4779819235982397489;
}
