// Os textos do quadro `AcessoNegado` (protótipo de autenticação, versão 58; spec `panel-roles`).
// Nenhum nomeia o papel: o do id token pode estar velho, e o que se sabe é que o servidor, ou a
// guarda pelo token, recusou (convenção 13; design.md da change papeis-no-painel, D3).

export const SEM_ACESSO_PAGINA = 'Você não tem acesso a esta página.';
export const SEM_PERMISSAO_ACAO = 'Você não tem permissão para esta ação.';
export const PRAZO_DO_PAPEL =
  'Se o seu papel mudou há pouco, a mudança pode levar até 15 minutos para chegar a todo o painel.';
