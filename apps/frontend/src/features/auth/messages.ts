// Textos das páginas de entrada (/entrar, /entrar/retorno, /convite, /redefinir-senha).

/** O aviso genérico do código recusado no /entrar/retorno (`?motivo=falha-na-entrada`). */
export const FALHA_NA_ENTRADA = 'Não foi possível concluir a entrada. Entre de novo.';
export const INICIANDO = 'O painel está iniciando. Tente de novo em alguns segundos.';
export const SEM_CONEXAO = 'Não foi possível falar com o servidor.';
export const MUITAS_TENTATIVAS = 'Muitas tentativas ao mesmo tempo. Tente de novo em instantes.';

/**
 * O `401` do `POST /auth/session/second-factor` sem passo pendente válido (cookie vencido,
 * verificação removida ou redefinida no meio). Copiado de
 * `apps/api/src/Buteco.Api/Users/Login/SessionEndpoints.cs` (`ExpiredMessage`): só o
 * `title` separa este `401` do "Código inválido.", e o painel o reconhece por igualdade
 * (decisão do dono, Q1 da change entrada-publica-painel). O teste
 * `SecondFactor_ExpiredPending_KeepsTheMessageThePanelRecognizes` do apps/api prende o
 * texto do lado do servidor. A troca por um campo de tipo é a #247.
 */
export const ENTRADA_EXPIROU = 'A entrada expirou. Informe e-mail e senha de novo.';

export const INFORME_O_CODIGO = 'Informe o código.';
export const CODIGO_INVALIDO = 'Código inválido.';
export const SENHA_SALVA_COM_CODIGO =
  'Senha salva. A sua conta usa verificação em 2 etapas: digite o código para entrar.';

export const POLITICA_DE_SENHA = 'A senha precisa ter de 12 a 128 caracteres.';
export const SENHAS_DIFERENTES = 'As senhas não conferem.';
export const ENVIO_INDISPONIVEL =
  'Não foi possível salvar agora. O que você digitou continua aqui: tente de novo em alguns segundos.';
