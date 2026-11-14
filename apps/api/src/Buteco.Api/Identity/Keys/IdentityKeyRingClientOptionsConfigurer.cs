using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Client;

namespace Buteco.Api.Identity.Keys;

/// <summary>
/// Entrega as chaves do <see cref="IdentityKeyRing"/> ao cliente OIDC dos provedores federados
/// (design.md da change provedores-federados-google, D1), no molde do
/// <see cref="IdentityKeyRingOptionsConfigurer"/>: o state token do cliente é cifrado e assinado pelas
/// mesmas chaves do emissor, sem chave nova. O state token tem tipo próprio, e o servidor recusa token
/// de outro tipo, então ele não se passa por nenhum token do emissor.
///
/// <para>
/// <b>Só lê o snapshot; nunca consulta o banco.</b> Antes da primeira carga não entrega nada, e a
/// montagem das opções lançaria (<c>ID0357</c>, <c>ID0358</c>); quem impede é o
/// <see cref="IdentityKeyRingGate"/>, como para o servidor.
/// </para>
/// </summary>
public sealed class IdentityKeyRingClientOptionsConfigurer(IdentityKeyRing ring) : IConfigureOptions<OpenIddictClientOptions>
{
    public void Configure(OpenIddictClientOptions options)
    {
        if (ring.Snapshot is not { } snapshot)
        {
            return;
        }

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

/// <summary>A troca do anel descarta as opções do cliente em cache, como as do servidor.</summary>
public sealed class IdentityKeyRingClientChangeTokenSource(IdentityKeyRing ring) : IOptionsChangeTokenSource<OpenIddictClientOptions>
{
    public string Name => Microsoft.Extensions.Options.Options.DefaultName;

    public IChangeToken GetChangeToken() => ring.GetChangeToken();
}
