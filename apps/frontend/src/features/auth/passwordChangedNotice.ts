// O aviso "Senha alterada." atravessa o logout do emissor pelo sessionStorage da aba
// (design.md da change minha-conta-painel, D3; decisão do dono Q1). O
// `post_logout_redirect_uri` registrado é exatamente `<painel>/entrar`, e o aviso não pode
// ir na query; o `state` do logout viraria um link que qualquer um monta. O logout volta na
// mesma aba, então a chave gravada antes dele chega ao /entrar.
//
// Minha conta grava antes de chamar o logout; o /entrar lê na montagem e apaga logo depois,
// num efeito: o StrictMode chama o inicializador do `useState` duas vezes, e apagar na
// leitura faria a segunda chamada não ver a chave.

export const PASSWORD_CHANGED_KEY = 'buteco.aviso.senha-alterada';

export function markPasswordChanged() {
  try {
    window.sessionStorage.setItem(PASSWORD_CHANGED_KEY, '1');
  } catch {
    // Sem sessionStorage, o /entrar abre sem o aviso; a troca já valeu.
  }
}

export function hasPasswordChanged(): boolean {
  try {
    return window.sessionStorage.getItem(PASSWORD_CHANGED_KEY) !== null;
  } catch {
    return false;
  }
}

export function clearPasswordChanged() {
  try {
    window.sessionStorage.removeItem(PASSWORD_CHANGED_KEY);
  } catch {
    // nada a apagar
  }
}
