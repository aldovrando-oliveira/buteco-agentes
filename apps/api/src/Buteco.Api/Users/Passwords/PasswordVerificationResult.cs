namespace Buteco.Api.Users.Passwords;

public enum PasswordVerificationResult
{
    Failed,
    Succeeded,

    /// <summary>A senha confere, e o hash gravado deve ser refeito com os parâmetros atuais.</summary>
    SucceededNeedsRehash,
}
