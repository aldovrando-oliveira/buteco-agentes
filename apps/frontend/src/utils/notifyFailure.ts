import { notifications } from '@mantine/notifications';
import { PRAZO_DO_PAPEL, SEM_PERMISSAO_ACAO } from '../components/feedback/accessTexts';
import { isRoleRefusal } from './failureNotice';

// A falha de uma ação das telas de domínio, no aviso vermelho onde ela já aparecia (spec
// `panel-roles`, "O 403 na carga e na ação"; design.md da change papeis-no-painel, D3). O `403`
// sem corpo é a recusa pelo papel, e diz isso com o texto do quadro `AcessoNegado`
// (`estado=acao`); qualquer outra falha mantém o texto da tela.
export function notifyFailure(error: unknown, fallback: { title: string; message: string }): void {
  if (isRoleRefusal(error)) {
    notifications.show({ color: 'red', title: SEM_PERMISSAO_ACAO, message: PRAZO_DO_PAPEL });
    return;
  }
  notifications.show({ color: 'red', ...fallback });
}
