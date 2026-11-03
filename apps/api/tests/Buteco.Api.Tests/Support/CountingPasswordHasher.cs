using Buteco.Api.Users.Passwords;

namespace Buteco.Api.Tests.Support;

/// <summary>
/// O <see cref="PasswordHasher"/> real, contando as verificações contra o hash fictício (design.md
/// da change convites-redefinicao-senha, D11). O guarda do usuário sem senha confere esse caminho,
/// e não o tempo: teste por tempo reprova sob carga (#226).
/// </summary>
public sealed class CountingPasswordHasher : PasswordHasher
{
    private int _dummyVerifications;

    public int DummyVerifications => Volatile.Read(ref _dummyVerifications);

    public void Reset() => Interlocked.Exchange(ref _dummyVerifications, 0);

    public override void VerifyAgainstDummy(string password)
    {
        Interlocked.Increment(ref _dummyVerifications);
        base.VerifyAgainstDummy(password);
    }
}
