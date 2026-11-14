namespace Buteco.Api.Identity.Federated;

/// <summary>
/// O ponto entre a leitura do convite e o consumo no retorno do aceite pelo provedor (design.md da change
/// aceite-convite-google, D4). Em produção não faz nada; os testes da concorrência o trocam por uma barreira,
/// para pôr um aceite por senha, um link novo, uma edição do e-mail ou outro vínculo exatamente nesse intervalo.
/// </summary>
public class FederatedInvitationPause
{
    public virtual Task BeforeConsumeAsync(Guid userId, CancellationToken cancellationToken) => Task.CompletedTask;
}
