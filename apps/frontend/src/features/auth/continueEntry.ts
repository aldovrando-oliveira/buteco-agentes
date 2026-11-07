import { beginLogin, followableAuthorizeReturn, safeReturnPath } from '../../auth/loginFlow';
import { authRuntime } from '../../auth/session';

/**
 * Depois que o `apps/api` gravou o cookie de sessão completo (o `204` da senha ou do
 * código), a URL do authorize para onde o painel navega. Com `retorno` (o authorize mandou
 * para cá), segue exatamente a string conferida. Fora da regra, o fluxo recomeça e termina
 * na tela inicial: o cookie recém-gravado devolve o código sem formulário. Sem `retorno`, o
 * fluxo é o normal, com o `voltarPara` (design.md da change painel-entra-pelo-emissor, D6).
 *
 * Extraído do EntrarPage para o `204` da senha, o `204` do código e as páginas do link
 * seguirem o mesmo caminho (design.md da change entrada-publica-painel, D1 e D3).
 */
export async function continueEntry(params: URLSearchParams): Promise<string> {
  const retorno = params.get('retorno');
  const seguir = followableAuthorizeReturn(retorno);
  return (
    seguir ??
    (await beginLogin(authRuntime, retorno === null ? safeReturnPath(params.get('voltarPara')) : '/'))
  );
}
