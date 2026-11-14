using Buteco.Api.Infrastructure;
using Buteco.Api.Users;
using Buteco.Api.Users.AccessLinks;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests.Support;

/// <summary>
/// Convidados e links gravados direto no banco, pela entidade (design.md da change
/// convites-redefinicao-senha, D2 e D4), para os testes que não exercitam a rota de convite.
/// </summary>
public static class InvitationSeed
{
    public sealed record Seeded(User User, string Token);

    public static async Task<Seeded> InvitedAsync(
        IServiceProvider services, Guid invitedBy, string? email = null, string name = "Pessoa Convidada", DateTimeOffset? expiresAt = null, UserRole role = UserRole.Admin)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
        var user = User.CreateInvitation(email ?? LocalUserSeed.UniqueEmail("convidado"), name, invitedBy, now, role);
        var token = AccessLinkToken.New();
        user.IssueAccessLink(AccessLinkPurpose.Invitation, AccessLinkToken.Hash(token), expiresAt ?? now + AccessLinkToken.InvitationLifetime);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return new Seeded(user, token);
    }

    /// <summary>Um link de redefinição para um usuário que já existe, gravado pela entidade.</summary>
    public static async Task<string> PasswordResetAsync(IServiceProvider services, Guid userId, DateTimeOffset? expiresAt = null)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
        var user = await db.Users.FindAsync(userId) ?? throw new InvalidOperationException("Usuário da semente não existe.");
        var token = AccessLinkToken.New();
        user.IssueAccessLink(AccessLinkPurpose.PasswordReset, AccessLinkToken.Hash(token), expiresAt ?? now + AccessLinkToken.PasswordResetLifetime);
        await db.SaveChangesAsync();
        return token;
    }
}
