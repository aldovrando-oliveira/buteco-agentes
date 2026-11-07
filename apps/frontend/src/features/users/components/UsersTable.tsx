import { Alert, Badge, Button, Group, Stack, Table, Text } from '@mantine/core';
import { SectionLabel } from '../../../components/data/SectionLabel';
import { formatRelative } from '../../../utils/panelDates';
import type { FailureNotice } from '../../../utils/failureNotice';
import type { UserSummary } from '../api/usersApi';
import { signInForms, stateBadge, twoFactorBadge, type Tom } from '../usersTexts';

// A tabela de Usuários (requisitos "Colunas da lista de Usuários" e "Ações da linha sem
// destino inexistente"; design.md da change usuarios-convites-identidade-painel, D2 e D8). As
// linhas vêm na ordem do servidor. A linha do ativo e a do desativado ficam sem ação até a
// #183: o "Editar" do quadro levaria a `/users/:id`, que ainda não existe.

const COR: Record<Tom, string> = { green: 'green', yellow: 'yellow', gray: 'gray', neutral: 'gray' };

function Selo({ texto, tom }: { texto: string; tom: Tom }) {
  // O neutro é o contorno, para nunca se confundir com o selo cinza de um estado conhecido.
  return (
    <Badge color={COR[tom]} variant={tom === 'neutral' ? 'outline' : 'light'} tt="none" data-tom={tom}>
      {texto}
    </Badge>
  );
}

export interface CancelRow {
  userId: string;
  enviando: boolean;
  aviso: FailureNotice | null;
}

interface UsersTableProps {
  users: UserSummary[];
  /** A linha do convite recém-criado, marcada enquanto o painel do link está aberto (Q8). */
  marcada: string | null;
  cancelando: CancelRow | null;
  onRenew: (user: UserSummary) => void;
  onAskCancel: (user: UserSummary) => void;
  onConfirmCancel: (user: UserSummary) => void;
  onBack: () => void;
}

export function UsersTable({
  users,
  marcada,
  cancelando,
  onRenew,
  onAskCancel,
  onConfirmCancel,
  onBack,
}: UsersTableProps) {
  return (
    <Table.ScrollContainer minWidth={760}>
      <Table>
        <Table.Thead bg="var(--buteco-surface-subtle)">
          <Table.Tr>
            {['Usuário', 'Formas de entrada', 'Verificação em 2 etapas', 'Estado', 'Último acesso', 'Ações'].map((c) => (
              <Table.Th key={c}>
                <SectionLabel>{c}</SectionLabel>
              </Table.Th>
            ))}
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {users.map((user) => {
            const estado = stateBadge(user);
            const mfa = twoFactorBadge(user);
            const formas = signInForms(user);
            const confirmando = cancelando?.userId === user.id;
            const convidado = user.estado === 'convidado';
            return (
              <Table.Tr
                key={user.id}
                data-marcada={marcada === user.id ? 'true' : undefined}
                bg={
                  marcada === user.id
                    ? 'var(--mantine-primary-color-light)'
                    : confirmando
                      ? 'var(--mantine-color-red-light)'
                      : undefined
                }
              >
                <Table.Td>
                  <Stack gap={0}>
                    <Text size="sm" fw={600}>
                      {user.nome}
                    </Text>
                    <Text size="xs" c="dimmed" ff="monospace">
                      {user.email}
                    </Text>
                  </Stack>
                </Table.Td>
                <Table.Td>
                  {formas.length === 0 ? (
                    <Text size="sm" c="dimmed">
                      —
                    </Text>
                  ) : (
                    <Group gap={6}>
                      {formas.map((f) => (
                        <Badge key={f.texto} variant={f.conhecido ? 'default' : 'outline'} color="gray" tt="none">
                          {f.texto}
                        </Badge>
                      ))}
                    </Group>
                  )}
                </Table.Td>
                <Table.Td>
                  {mfa.texto === '—' ? (
                    <Text size="sm" c="dimmed">
                      —
                    </Text>
                  ) : (
                    <Selo {...mfa} />
                  )}
                </Table.Td>
                <Table.Td>
                  <Stack gap={2} align="flex-start">
                    <Selo texto={estado.texto} tom={estado.tom} />
                    {estado.abaixo && (
                      <Text size="xs" c="dimmed">
                        {estado.abaixo}
                      </Text>
                    )}
                  </Stack>
                </Table.Td>
                <Table.Td>
                  <Text size="sm" c="dimmed" style={{ whiteSpace: 'nowrap' }}>
                    {user.ultimoAcesso ? formatRelative(user.ultimoAcesso) : '—'}
                  </Text>
                </Table.Td>
                <Table.Td style={{ whiteSpace: 'nowrap' }}>
                  {convidado && confirmando && (
                    <Stack gap={6}>
                      <Group gap="sm" wrap="nowrap">
                        <Text size="sm" c="red">
                          Cancelar o convite? O link deixa de funcionar.
                        </Text>
                        <Button size="xs" color="red" loading={cancelando.enviando} onClick={() => onConfirmCancel(user)}>
                          Cancelar convite
                        </Button>
                        <Button size="xs" variant="subtle" onClick={onBack} disabled={cancelando.enviando}>
                          Voltar
                        </Button>
                      </Group>
                      {cancelando.aviso && (
                        <Alert color={cancelando.aviso.cor} role="alert" p="xs">
                          <Text size="xs">{cancelando.aviso.texto}</Text>
                        </Alert>
                      )}
                    </Stack>
                  )}
                  {convidado && !confirmando && (
                    <Group gap="sm" wrap="nowrap">
                      <Button
                        size="xs"
                        variant="default"
                        aria-label={`Gerar novo link para ${user.nome}`}
                        onClick={() => onRenew(user)}
                      >
                        Gerar novo link
                      </Button>
                      <Button
                        size="xs"
                        variant="subtle"
                        color="red"
                        aria-label={`Cancelar convite de ${user.nome}`}
                        onClick={() => onAskCancel(user)}
                      >
                        Cancelar convite
                      </Button>
                    </Group>
                  )}
                </Table.Td>
              </Table.Tr>
            );
          })}
        </Table.Tbody>
      </Table>
    </Table.ScrollContainer>
  );
}
