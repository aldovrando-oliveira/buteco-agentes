import type { Operacao } from './types';

// Os textos das telas de aplicações externas, do protótipo `H7dPBZafiSkdKd1hqesPZ6` na versão 66
// (design.md da change aplicacoes-externas-painel, "A versão 65" e as divergências aceitas).

export const ROTULO_OPERACAO: Record<Operacao, string> = {
  'enviar-mensagem': 'Enviar mensagem',
  'consultar-task': 'Consultar task',
  'notificacao-push': 'Notificação push',
};

export const DESCRICAO_OPERACAO: Record<Operacao, string> = {
  'enviar-mensagem': 'Inicia conversa ou task',
  'consultar-task': 'Acompanha o andamento',
  'notificacao-push': 'Aviso ao concluir',
};

/** As operações por extenso, como o card do agente: "Enviar mensagem · Consultar task". */
export const operacoesPorExtenso = (ops: readonly Operacao[]) =>
  ops.map((op) => ROTULO_OPERACAO[op]).join(' · ');

// ---------------------------------------------------------------- tempo

const MINUTO = 60 * 1000;
const HORA = 60 * MINUTO;
const DIA = 24 * HORA;

/**
 * O "há 3 min" do quadro. O painel tinha só o "hoje, HH:MM" de Minha conta (`panelDates`), que o
 * quadro de aplicações não usa: o último uso de uma credencial lê melhor como distância.
 */
export function formatAgo(iso: string, now: Date = new Date()): string {
  const passou = Math.max(0, now.getTime() - new Date(iso).getTime());
  if (passou < MINUTO) return 'há menos de 1 min';
  if (passou < HORA) return `há ${Math.floor(passou / MINUTO)} min`;
  if (passou < DIA) return `há ${Math.floor(passou / HORA)} h`;
  const dias = Math.floor(passou / DIA);
  if (dias < 365) return dias === 1 ? 'há 1 dia' : `há ${dias} dias`;
  const anos = Math.floor(dias / 365);
  return anos === 1 ? 'há 1 ano' : `há ${anos} anos`;
}

// ---------------------------------------------------------------- lista

export const LISTA_SUBTITULO =
  'Sistemas que chamam agentes pelo protocolo A2A. Cada aplicação só alcança os agentes liberados para ela.';
export const LISTA_VAZIA = 'Nenhuma aplicação externa cadastrada.';
export const LISTA_VAZIA_TEXTO =
  'Cadastre uma aplicação para um sistema de fora chamar agentes pelo A2A.';
export const LISTA_ERRO = 'Não foi possível carregar as aplicações.';
export const NUNCA_USADA = 'Nunca usada';

export const rodapeDaLista = (n: number) =>
  `${n === 1 ? '1 aplicação' : `${n} aplicações`} · as desativadas continuam listadas e podem ser reativadas.`;

export function semResultado(busca: string, estado: 'todas' | 'ativas' | 'desativadas'): string {
  const termo = busca.trim();
  const entre = estado === 'todas' ? '' : ` entre as ${estado}`;
  return termo
    ? `Nenhuma aplicação encontrada para "${termo}"${entre}.`
    : `Nenhuma aplicação encontrada${entre}.`;
}

// ---------------------------------------------------------------- nova e editar

export const DICA_NOME = 'Até 80 caracteres, sem repetir o nome de outra aplicação.';
export const DICA_DESCRICAO = 'Opcional, até 280 caracteres.';
export const INFORME_O_NOME = 'Informe o nome.';
export const NOME_LONGO = 'O nome tem no máximo 80 caracteres.';
export const DESCRICAO_LONGA = 'A descrição tem no máximo 280 caracteres.';
export const NOME_REPETIDO = 'Já existe uma aplicação com este nome.';
export const DICA_SENHA_CRIAR = 'Criar a aplicação gera uma credencial. Confirme com a sua senha.';
export const NASCE_SEM_AGENTES_ANTES = 'A aplicação nasce ';
export const NASCE_SEM_AGENTES_DESTAQUE = 'sem nenhum agente liberado';
export const NASCE_SEM_AGENTES_DEPOIS =
  ': até você liberar agentes na tela dela, toda chamada A2A é recusada.';
export const ENDPOINT_DEPOIS_DE_CRIAR =
  'O endereço em que a aplicação pede o token (client credentials) aparece depois de criar.';
export const EDITAR_NAO_MUDA_CREDENCIAL = 'O client ID e os segredos não mudam ao editar.';

// ---------------------------------------------------------------- segredo mostrado uma vez

export const SEGREDO_SO_AGORA_DEPOIS =
  '. O Buteco guarda apenas o hash e não consegue mostrá-lo de novo.';
export const SE_SE_PERDER = ' Se ele se perder, gere um novo na aba Credencial e conexão.';
export const GUARDEI = 'Guardei o segredo em um local seguro';
export const anteriorValeAte = (data: string) => ` O anterior continua valendo até ${data}.`;

// ---------------------------------------------------------------- detalhe

export const APLICACAO_NAO_ENCONTRADA = 'Aplicação não encontrada.';
export const DETALHE_ERRO = 'Não foi possível carregar a aplicação.';
export const confirmarDesativar = (nome: string) =>
  `Desativar ${nome}? Todos os segredos dela são revogados e as chamadas A2A são recusadas na hora. Os agentes liberados ficam guardados. Ao reativar, você precisa gerar um segredo novo.`;
export const REATIVADA = 'Aplicação reativada. Gere um segredo para ela voltar a obter token.';

// ---------------------------------------------------------------- credencial e conexão

export const ROTULO_ESTADO_SEGREDO = {
  ativo: 'Ativo',
  substituido: 'Substituído',
  revogado: 'Revogado',
  expirado: 'Expirado',
} as const;
export const NUNCA_USADO = 'Nunca usado';
export const RODAPE_SEGREDOS =
  'Ao gerar um novo segredo, o anterior continua valendo por 7 dias para a aplicação trocar sem parar. Revogar recusa o segredo na hora; um token já emitido continua valendo por até 5 minutos. Cada aplicação tem no máximo 3 segredos vivos, e cada segredo vale 1 ano.';
export const SEM_SEGREDO_VIVO =
  'Nenhum segredo válido: a aplicação não consegue obter token. Gere um novo segredo.';
export const DICA_LIMITE = 'Já há 3 segredos válidos. Revogue um para gerar outro.';
export const DICA_DESATIVADA = 'Reative a aplicação para gerar um segredo.';
/** Só com segredo vivo, e "até": o anterior vale `min(validoAte, agora + 7 dias)` (divergência 4, aceita). */
export const ATUAL_VALE_ATE_7_DIAS =
  'O segredo atual continua valendo por até 7 dias, para a aplicação trocar sem parar.';
export const DICA_SENHA_GERAR =
  'Gerar um segredo cria uma credencial nova. Confirme com a sua senha.';

export const confirmarRevogar = (sufixo: string, unicoVivo: boolean) =>
  `Revogar o segredo btc_sk_••••${sufixo}? A aplicação deixa de obter token com ele na hora; um token já emitido continua valendo por até 5 minutos.` +
  (unicoVivo ? ' Ela fica sem segredo válido até você gerar outro.' : '');
export const confirmarRevogarAgora = (sufixo: string, valiaAte: string) =>
  `Revogar agora o segredo btc_sk_••••${sufixo}? Ele valeria até ${valiaAte}. A aplicação deixa de obter token com ele na hora; um token já emitido continua valendo por até 5 minutos.`;

export const CONEXAO_TEXTO =
  'A chamada só é aceita para os agentes liberados na aba Agentes; para os outros, a resposta é recusada. O endereço de cada agente liberado também está no AgentCard público: ';

// ---------------------------------------------------------------- agentes

export const AGENTES_VAZIO =
  'Nenhum agente liberado. Enquanto a lista estiver vazia, toda chamada A2A desta aplicação é recusada.';
export const AGENTES_ERRO = 'Não foi possível carregar os agentes.';
export const DELEGACAO = 'Agentes alcançados por delegação não precisam ser liberados.';
export const TODOS_LIBERADOS = 'Todos os agentes já estão liberados para esta aplicação.';
export const AGENTE_INATIVO_AVISO =
  'Este agente está inativo. Ele fica liberado, mas as tasks que a aplicação abrir nele terminam recusadas até ele ser ativado.';
export const MARQUE_UMA = 'Marque ao menos uma operação.';
export const MARQUE_UMA_LINHA = 'Marque ao menos uma operação';
export const AGENTE_NAO_ENCONTRADO = 'Agente não encontrado.';
export const VINCULO_RETIRADO = 'Agente não liberado para esta aplicação. A lista foi atualizada.';
export const AVISO_DESATIVADA_AGENTES =
  'Aplicação desativada: as chamadas A2A dela são recusadas. Os agentes liberados ficam guardados e voltam a valer ao reativar.';
export const confirmarRemover = (agente: string) =>
  `Remover o acesso da aplicação a ${agente}? A próxima chamada dela a este agente é recusada; as tasks já abertas não mudam.`;

// ---------------------------------------------------------------- card do agente

export const CARD_ERRO = 'Não foi possível carregar as aplicações.';
export const NENHUMA_ADMIN = 'Nenhuma aplicação externa tem acesso a este agente.';
export const NENHUMA_OUTROS = 'Nenhuma aplicação externa usa este agente.';
export const usadoPorAtivas = (ativas: number) =>
  ativas === 1 ? '1 aplicação externa ativa' : `${ativas} aplicações externas ativas`;
export const todasDesativadas = (total: number) =>
  `Usado por ${total === 1 ? '1 aplicação externa, desativada' : `${total} aplicações externas, todas desativadas`}.`;
export const aplicacoesAtivas = (n: number) =>
  n === 1 ? '1 aplicação ativa' : `${n} aplicações ativas`;
export const AFETA_ANTES = 'Desativar este agente afeta ';
export const AFETA_DEPOIS = ': as tasks que elas abrirem nele passam a terminar recusadas.';
/** O singular da versão 68 do protótipo: "…afeta 1 aplicação ativa: as tasks que ela abrir…". */
export const AFETA_DEPOIS_UMA = ': as tasks que ela abrir nele passam a terminar recusadas.';
export const usoDaAplicacao = (ultimoUso: string | null, now?: Date) =>
  ultimoUso ? `aplicação usada ${formatAgo(ultimoUso, now)}` : 'aplicação nunca usada';
