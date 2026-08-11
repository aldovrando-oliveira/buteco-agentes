namespace Buteco.Inbox.Channels.Adapters.Testing;

// Adapter de teste desta fatia (design.md, Decision 6) — prova que
// CreateChannelCommandHandler/UpdateChannelCommandHandler chamam o
// validador registrado para o ChannelType certo, sem modelar nenhum
// formato real de credencial. Schema fictício: rejeita só o valor
// sentinela "invalid-credential", usado exclusivamente pelos testes que
// exercitam o caminho de rejeição — qualquer outro valor não vazio é
// aceito, para não forçar reformatação das credenciais já usadas em
// testes existentes que não têm relação com esta validação.
public sealed class TestChannelConfigValidator : IChannelConfigValidator
{
    public const string RejectedCredential = "invalid-credential";

    public ChannelConfigValidationResult Validate(string credential)
    {
        if (string.IsNullOrWhiteSpace(credential) || credential == RejectedCredential)
        {
            return ChannelConfigValidationResult.Failure(new Dictionary<string, string[]>
            {
                ["credential"] = ["Credencial rejeitada pelo adapter de teste (test-channel)."],
            });
        }

        return ChannelConfigValidationResult.Success();
    }
}
