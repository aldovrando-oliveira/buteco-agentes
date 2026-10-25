using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;

namespace Buteco.Api.Identity.Keys;

/// <summary>
/// Entrega as chaves do <see cref="IdentityKeyRing"/> às opções do OpenIddict (design.md
/// da change emissor-oidc-api, D5 e D6). <b>Só lê o snapshot; nunca consulta o banco</b>:
/// a fábrica de opções roda dentro da requisição, e uma falha aqui ficaria presa no
/// cache de opções.
///
/// <para>
/// Antes da primeira carga não entrega nada, e a montagem das opções lançaria
/// (<c>ID0085</c>). Quem impede que isso aconteça é o <see cref="IdentityKeyRingGate"/>,
/// que não deixa requisição nenhuma chegar ao <c>UseAuthentication</c> antes da carga.
/// </para>
/// </summary>
public sealed class IdentityKeyRingOptionsConfigurer(IdentityKeyRing ring) : IConfigureOptions<OpenIddictServerOptions>
{
    public void Configure(OpenIddictServerOptions options)
    {
        if (ring.Snapshot is { } snapshot)
        {
            Apply(options, snapshot);
        }
    }

    public static void Apply(OpenIddictServerOptions options, IdentityKeyRingSnapshot snapshot)
    {
        foreach (var key in snapshot.SigningKeys)
        {
            options.SigningCredentials.Add(new SigningCredentials(key, IdentityKeyFactory.SigningAlgorithm));
        }

        foreach (var key in snapshot.EncryptionKeys)
        {
            options.EncryptionCredentials.Add(new EncryptingCredentials(
                key, IdentityKeyFactory.EncryptionAlgorithm, SecurityAlgorithms.Aes256CbcHmacSha512));
        }
    }
}

/// <summary>
/// Faz o <c>IOptionsMonitor&lt;OpenIddictServerOptions&gt;</c> descartar as opções em cache
/// quando o anel muda, e assim a troca de quem assina acontece sem reiniciar (D7, F4).
/// </summary>
public sealed class IdentityKeyRingChangeTokenSource(IdentityKeyRing ring) : IOptionsChangeTokenSource<OpenIddictServerOptions>
{
    public string Name => Microsoft.Extensions.Options.Options.DefaultName;

    public IChangeToken GetChangeToken() => ring.GetChangeToken();
}
