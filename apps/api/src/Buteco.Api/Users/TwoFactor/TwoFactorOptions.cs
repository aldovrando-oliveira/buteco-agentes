namespace Buteco.Api.Users.TwoFactor;

/// <summary>
/// <c>Identity:TwoFactor</c> (design.md da change verificacao-2-etapas-totp, D1). O cadastro
/// da verificação em 2 etapas fica fechado até o painel ter a tela do código (#183): o painel
/// de hoje só conhece o <c>204</c> do <c>POST /auth/session</c>, e quem ligasse a verificação
/// ficaria sem entrar. A verificação no login, a remoção, a regeneração dos códigos e a
/// redefinição pelo admin nunca fecham. A opção existe só para essa transição, e a #220 a
/// remove com a obrigatoriedade.
/// </summary>
public sealed class TwoFactorOptions
{
    public const string SectionName = "Identity:TwoFactor";

    public bool EnrollmentEnabled { get; set; }
}
