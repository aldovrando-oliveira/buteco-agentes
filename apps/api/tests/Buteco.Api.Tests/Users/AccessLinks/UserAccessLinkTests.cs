using Buteco.Api.Users;
using Buteco.Api.Users.AccessLinks;

namespace Buteco.Api.Tests.Users.AccessLinks;

/// <summary>
/// O convidado e o link pendente na entidade (design.md da change convites-redefinicao-senha,
/// D2 e D4).
///
/// <para>
/// Verificado contra o defeito (tasks.md, 3.3): <c>Deactivate</c> sem apagar o link →
/// <see cref="Deactivate_ClearsThePendingLink"/> reprova.
/// </para>
/// </summary>
public class UserAccessLinkTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private static User ActiveWithResetLink()
    {
        var user = User.Create("ativa@exemplo.test", "Ativa", "$argon2id$qualquer", Now);
        user.IssueAccessLink(AccessLinkPurpose.PasswordReset, [1, 2, 3], Now.AddHours(24));
        return user;
    }

    private static void AssertNoLink(User user)
    {
        Assert.Null(user.AccessLinkHash);
        Assert.Null(user.AccessLinkPurpose);
        Assert.Null(user.AccessLinkExpiresAt);
    }

    [Fact]
    public void TheInvitedIsBornWithoutPassword_WithTheInviter_AndNotActive()
    {
        var inviter = Guid.NewGuid();

        var user = User.CreateInvitation("  Convidada@Exemplo.test ", "  Convidada  ", inviter, Now);

        Assert.Equal(UserStatus.Invited, user.Status);
        Assert.True(user.IsInvited);
        Assert.False(user.IsActive);
        Assert.Null(user.PasswordHash);
        Assert.Equal(inviter, user.InvitedBy);
        Assert.Equal("Convidada@Exemplo.test", user.Email);
        Assert.Equal("convidada@exemplo.test", user.NormalizedEmail);
        Assert.Equal("Convidada", user.Name);
        Assert.Equal(Now, user.CreatedAt);
    }

    [Fact]
    public void IssuingAnotherLink_OverwritesTheFirst()
    {
        var user = ActiveWithResetLink();

        user.IssueAccessLink(AccessLinkPurpose.PasswordReset, [9, 9], Now.AddHours(30));

        Assert.Equal([9, 9], user.AccessLinkHash);
        Assert.Equal(Now.AddHours(30), user.AccessLinkExpiresAt);
    }

    [Fact]
    public void Deactivate_ClearsThePendingLink()
    {
        var user = ActiveWithResetLink();

        user.Deactivate(Now);
        AssertNoLink(user);

        user.Activate();
        AssertNoLink(user);
    }

    [Fact]
    public void ChangePassword_ClearsThePendingLink()
    {
        var user = ActiveWithResetLink();

        user.ChangePassword("$argon2id$outra");

        AssertNoLink(user);
    }
}
