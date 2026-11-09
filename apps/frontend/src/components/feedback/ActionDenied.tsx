import { Alert, Text } from '@mantine/core';
import { PRAZO_DO_PAPEL, SEM_PERMISSAO_ACAO } from './accessTexts';

/**
 * O aviso da ação recusada pelo papel (quadro `AcessoNegado`, `estado=acao`; spec
 * `panel-roles`): vermelho, no lugar onde a ação mostra a falha. É o do `403` sem corpo, o da
 * tabela de papéis dos três apps; o `403` com `title` mostra o `title` (os "a si mesmo" de
 * Editar usuário).
 */
export function ActionDenied({ maw }: { maw?: number }) {
  return (
    <Alert color="red" role="alert" maw={maw} title={SEM_PERMISSAO_ACAO}>
      <Text size="sm">{PRAZO_DO_PAPEL}</Text>
    </Alert>
  );
}
