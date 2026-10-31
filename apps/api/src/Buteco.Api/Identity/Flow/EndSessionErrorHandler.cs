using Microsoft.AspNetCore;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Buteco.Api.Identity.Flow;

/// <summary>
/// O encerramento de sessão sempre termina (design.md da change
/// usuarios-locais-login-emissor, D3.9; 2.4 (b)). Um pedido recusado pelo OpenIddict, como
/// <c>post_logout_redirect_uri</c> não registrado, nunca chega à rota em passthrough. O hint
/// inválido não é recusado (ele é opcional) e chega à rota. Sem este handler, a resposta seria o erro local do
/// OpenIddict, e o cookie <c>buteco-sessao</c> ficaria.
///
/// <para>
/// O <c>EnableErrorPassthrough</c> não serve: a opção é global e mandaria à rota também os
/// erros do authorize (<c>OpenIddictServerAspNetCoreHandlers.Authentication.cs</c> l. 42),
/// que hoje são recusados antes dela (2.4 (a)). Então, só na resposta de encerramento com
/// erro: apaga o cookie e redireciona para <c>/entrar</c>, sem nunca usar o endereço pedido.
/// Roda logo depois do código HTTP e do cache, antes de qualquer processamento de resposta.
/// </para>
/// </summary>
public sealed class EndSessionErrorHandler : IOpenIddictServerHandler<ApplyEndSessionResponseContext>
{
    public static OpenIddictServerHandlerDescriptor Descriptor { get; }
        = OpenIddictServerHandlerDescriptor.CreateBuilder<ApplyEndSessionResponseContext>()
            .UseSingletonHandler<EndSessionErrorHandler>()
            .SetOrder(OpenIddictServerAspNetCoreHandlers.AttachCacheControlHeader<ApplyEndSessionResponseContext>.Descriptor.Order + 500)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public ValueTask HandleAsync(ApplyEndSessionResponseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrEmpty(context.Transaction.Response?.Error) ||
            context.Transaction.GetHttpRequest()?.HttpContext is not { } httpContext)
        {
            return ValueTask.CompletedTask;
        }

        // Os mesmos atributos do cookie gravado, para o navegador casar a remoção.
        var development = httpContext.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment();
        httpContext.Response.Cookies.Delete(IssuerSession.CookieName, new CookieOptions
        {
            Path = "/",
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = !development || httpContext.Request.IsHttps,
        });
        httpContext.Response.StatusCode = StatusCodes.Status302Found;
        httpContext.Response.Headers.Location = AuthorizeEndpoint.LoginPath;
        context.HandleRequest();

        return ValueTask.CompletedTask;
    }
}
