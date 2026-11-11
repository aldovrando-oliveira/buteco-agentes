import { Fragment, useState } from 'react';
import { Alert, Badge, Box, Button, Code, Group, Stack, Table, Text, Title } from '@mantine/core';
import { useQueryClient } from '@tanstack/react-query';
import { SectionedCard } from '../../../components/data/SectionedCard';
import { ActionDenied } from '../../../components/feedback/ActionDenied';
import {
  failureNotice,
  isApiFailure,
  isRoleRefusal,
  type FailureNotice,
} from '../../../utils/failureNotice';
import { formatDate } from '../../../utils/panelDates';
import { useCallerPassword } from '../../users/callerPassword';
import { CallerPasswordField } from '../../users/components/CallerPasswordField';
import { ConfirmBar } from '../../users/components/ConfirmBar';
import { generateSecret, revokeSecret } from '../api/applicationsApi';
import { APPLICATIONS_KEY } from '../queryKeys';
import type { ApplicationDetail, CreatedSecret, SecretSummary } from '../types';
import {
  ATUAL_VALE_ATE_7_DIAS,
  CONEXAO_TEXTO,
  DICA_DESATIVADA,
  DICA_LIMITE,
  DICA_SENHA_GERAR,
  NUNCA_USADO,
  RODAPE_SEGREDOS,
  ROTULO_ESTADO_SEGREDO,
  SEM_SEGREDO_VIVO,
  anteriorValeAte,
  confirmarRevogar,
  confirmarRevogarAgora,
  formatAgo,
} from '../applicationsTexts';
import { CopyableValue, OnlyNowNotice, SavedGate } from './SecretOnce';
import { estaVivo } from '../applicationFields';

// A aba "Credencial e conexão" (quadro `AplicacaoDetalhe`, `aba=identidade`, versão 66; spec
// `panel-external-applications`, "Aba Credencial e conexão", "Gerar novo segredo", "Revogar com
// confirmação na linha" e "Uma ação por vez"). O contrato é o da #186: os estados vêm do servidor,
// e "vivo" é `ativo` ou `substituido` — os dois emitem token (D4).

const COR_ESTADO = {
  ativo: 'green',
  substituido: 'yellow',
  revogado: 'gray',
  expirado: 'gray',
} as const;
const LIMITE_DE_VIVOS = 3;

type Modo =
  | { tipo: 'nenhum' }
  | { tipo: 'gerar' }
  | {
      tipo: 'revogar';
      segredoId: string;
      enviando: boolean;
      falha: FailureNotice | null;
      recusada: boolean;
    };

interface ApplicationCredentialTabProps {
  app: ApplicationDetail;
  /** O segredo recém-gerado, no estado da página do detalhe (D3). */
  segredoNovo: CreatedSecret | null;
  onSegredoNovo: (segredo: CreatedSecret | null) => void;
}

export function ApplicationCredentialTab({
  app,
  segredoNovo,
  onSegredoNovo,
}: ApplicationCredentialTabProps) {
  const queryClient = useQueryClient();
  const [modo, setModo] = useState<Modo>({ tipo: 'nenhum' });
  const vivos = app.segredos.filter(estaVivo);
  const ocupado = modo.tipo !== 'nenhum' || segredoNovo !== null;
  const dica = !app.ativa ? DICA_DESATIVADA : vivos.length >= LIMITE_DE_VIVOS ? DICA_LIMITE : null;
  const reler = () => queryClient.invalidateQueries({ queryKey: APPLICATIONS_KEY });

  const revogar = async (segredo: SecretSummary) => {
    setModo({
      tipo: 'revogar',
      segredoId: segredo.id,
      enviando: true,
      falha: null,
      recusada: false,
    });
    try {
      await revokeSecret(app.id, segredo.id);
      setModo({ tipo: 'nenhum' });
      await reler();
    } catch (error) {
      const recusada = isRoleRefusal(error);
      setModo({
        tipo: 'revogar',
        segredoId: segredo.id,
        enviando: false,
        falha: recusada ? null : failureNotice(error),
        recusada,
      });
    }
  };

  const textoDaConfirmacao = (segredo: SecretSummary) =>
    segredo.estado === 'substituido'
      ? confirmarRevogarAgora(segredo.sufixo, formatDate(segredo.validoAte))
      : confirmarRevogar(segredo.sufixo, vivos.length === 1);

  const substituidoMaisNovo = app.segredos.find((s) => s.estado === 'substituido');

  return (
    <Stack>
      <SectionedCard
        title="Credencial do Buteco"
        action={
          <Group gap="xs" wrap="nowrap">
            {dica && (
              <Text size="xs" c="dimmed">
                {dica}
              </Text>
            )}
            <Button
              size="xs"
              onClick={() => setModo({ tipo: 'gerar' })}
              disabled={ocupado || dica !== null}
            >
              Gerar novo segredo
            </Button>
          </Group>
        }
      >
        {modo.tipo === 'gerar' && (
          <GenerateSecretForm
            app={app}
            temVivo={vivos.length > 0}
            onCancel={() => setModo({ tipo: 'nenhum' })}
            onGerado={(segredo) => {
              setModo({ tipo: 'nenhum' });
              onSegredoNovo(segredo);
              void reler();
            }}
          />
        )}

        {segredoNovo && (
          <Box
            px="md"
            py="md"
            bg="var(--mantine-primary-color-light)"
            style={{ borderBottom: '1px solid var(--mantine-color-default-border)' }}
          >
            <Stack gap="sm" maw={640}>
              <Title order={3} fz="sm" fw={600}>
                Segredo novo
              </Title>
              <CopyableValue label="Segredo" value={segredoNovo.valor} />
              <OnlyNowNotice
                complemento={
                  substituidoMaisNovo
                    ? anteriorValeAte(formatDate(substituidoMaisNovo.validoAte))
                    : ''
                }
              />
              <SecretDoneGate onConcluir={() => onSegredoNovo(null)} />
            </Stack>
          </Box>
        )}

        {app.ativa && vivos.length === 0 && (
          <Box px="md" pt="sm">
            <Alert color="yellow" role="status" p="sm">
              <Text size="sm">{SEM_SEGREDO_VIVO}</Text>
            </Alert>
          </Box>
        )}

        <SectionedCard.Body>
          <Group gap="xl" py={6} wrap="wrap">
            <Stack gap={2}>
              <Text size="xs" c="dimmed">
                Client ID
              </Text>
              <Text ff="monospace" size="sm">
                {app.clientId}
              </Text>
            </Stack>
            <Stack gap={2} style={{ minWidth: 0 }}>
              <Text size="xs" c="dimmed">
                Endpoint de token
              </Text>
              <Text ff="monospace" size="sm" style={{ overflowWrap: 'anywhere' }}>
                {app.endpointToken}
              </Text>
            </Stack>
          </Group>
        </SectionedCard.Body>

        <Box style={{ overflowX: 'auto' }}>
          <Table miw={760} verticalSpacing="xs" horizontalSpacing="md">
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Segredo</Table.Th>
                <Table.Th>Estado</Table.Th>
                <Table.Th>Criado em</Table.Th>
                <Table.Th>Válido até</Table.Th>
                <Table.Th>Último uso</Table.Th>
                <Table.Th>Ações</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {app.segredos.map((segredo) => {
                const confirmando =
                  modo.tipo === 'revogar' && modo.segredoId === segredo.id ? modo : null;
                const acao =
                  segredo.estado === 'ativo'
                    ? 'Revogar'
                    : segredo.estado === 'substituido'
                      ? 'Revogar agora'
                      : null;
                return (
                  <Fragment key={segredo.id}>
                    <Table.Tr>
                      <Table.Td>
                        <Text ff="monospace" size="sm">
                          btc_sk_••••{segredo.sufixo}
                        </Text>
                      </Table.Td>
                      <Table.Td>
                        <Badge color={COR_ESTADO[segredo.estado]} variant="light">
                          {ROTULO_ESTADO_SEGREDO[segredo.estado]}
                        </Badge>
                      </Table.Td>
                      <Table.Td>{formatDate(segredo.criadoEm)}</Table.Td>
                      <Table.Td>{formatDate(segredo.validoAte)}</Table.Td>
                      <Table.Td>
                        {segredo.ultimoUso ? formatAgo(segredo.ultimoUso) : NUNCA_USADO}
                      </Table.Td>
                      <Table.Td>
                        {acao ? (
                          <Button
                            size="compact-sm"
                            variant="subtle"
                            color="red"
                            disabled={ocupado}
                            aria-label={`${acao} btc_sk_••••${segredo.sufixo}`}
                            onClick={() =>
                              setModo({
                                tipo: 'revogar',
                                segredoId: segredo.id,
                                enviando: false,
                                falha: null,
                                recusada: false,
                              })
                            }
                          >
                            {acao}
                          </Button>
                        ) : (
                          <Text c="dimmed">—</Text>
                        )}
                      </Table.Td>
                    </Table.Tr>
                    {confirmando && acao && (
                      <Table.Tr>
                        <Table.Td colSpan={6} p={0}>
                          <ConfirmBar
                            texto={textoDaConfirmacao(segredo)}
                            confirmLabel={acao}
                            onConfirm={() => void revogar(segredo)}
                            onCancel={() => setModo({ tipo: 'nenhum' })}
                            enviando={confirmando.enviando}
                            falha={confirmando.falha}
                          />
                          {confirmando.recusada && <ActionDenied />}
                        </Table.Td>
                      </Table.Tr>
                    )}
                  </Fragment>
                );
              })}
            </Table.Tbody>
          </Table>
        </Box>
        <SectionedCard.Body>
          <Text size="xs" c="dimmed">
            {RODAPE_SEGREDOS}
          </Text>
        </SectionedCard.Body>
      </SectionedCard>

      <SectionedCard title="Como a aplicação se conecta">
        <SectionedCard.Body>
          <Stack gap="sm" py={6}>
            <Code block>{curlDeConexao(app)}</Code>
            <Text size="xs" c="dimmed">
              {CONEXAO_TEXTO}
              <Text span ff="monospace" size="xs">
                /agents/[AGENT_ID]/.well-known/agent-card.json
              </Text>
              .
            </Text>
          </Stack>
        </SectionedCard.Body>
      </SectionedCard>
    </Stack>
  );
}

function curlDeConexao(app: ApplicationDetail) {
  const base = app.endpointToken.replace(/connect\/token$/, '');
  return [
    '# 1. Obter o token no Buteco (client credentials)',
    `curl -X POST ${app.endpointToken} \\`,
    '  -d grant_type=client_credentials \\',
    `  -d client_id=${app.clientId} \\`,
    '  -d client_secret=$CLIENT_SECRET \\',
    '  -d scope=buteco-api',
    '# 2. Chamar o agente por A2A com o token',
    `curl -X POST ${base}agents/[AGENT_ID]/a2a \\`,
    '  -H "Authorization: Bearer $ACCESS_TOKEN" \\',
    '  -H "Content-Type: application/json" \\',
    `  -d '{"jsonrpc":"2.0","id":1,"method":"SendMessage","params":{...}}'`,
  ].join('\n');
}

/** "Guardei o segredo…" e "Concluir", que fecha o bloco do segredo novo. */
function SecretDoneGate({ onConcluir }: { onConcluir: () => void }) {
  const [guardei, setGuardei] = useState(false);
  return (
    <SavedGate guardei={guardei} onGuardei={setGuardei}>
      {(desabilitado) => (
        <Button size="sm" onClick={onConcluir} disabled={desabilitado}>
          Concluir
        </Button>
      )}
    </SavedGate>
  );
}

interface GenerateSecretFormProps {
  app: ApplicationDetail;
  temVivo: boolean;
  onCancel: () => void;
  onGerado: (segredo: CreatedSecret) => void;
}

/**
 * O formulário da senha dentro do cartão. A chamada é direta, pelo `useCallerPassword`, NUNCA por
 * `useMutation`: a resposta traz o segredo, e o `MutationCache` a guardaria (D3).
 */
function GenerateSecretForm({ app, temVivo, onCancel, onGerado }: GenerateSecretFormProps) {
  const senha = useCallerPassword();
  const queryClient = useQueryClient();
  const [erro, setErro] = useState<string | null>(null);
  const [recusado, setRecusado] = useState(false);

  const gerar = async () => {
    setErro(null);
    setRecusado(false);
    const result = await senha.enviar(
      (s) => generateSecret(app.id, s),
      (error) => {
        if (!isApiFailure(error)) return false;
        if (isRoleRefusal(error)) {
          setRecusado(true);
          return true;
        }
        if (error.status === 409 || error.status === 404) {
          setErro(error.title ?? 'Não foi possível gerar o segredo.');
          void queryClient.invalidateQueries({ queryKey: APPLICATIONS_KEY });
          return true;
        }
        return false;
      },
    );
    if (result) onGerado(result);
  };

  return (
    <Box
      component="form"
      noValidate
      aria-labelledby="gerar-segredo"
      px="md"
      py="md"
      bg="var(--mantine-primary-color-light)"
      style={{ borderBottom: '1px solid var(--mantine-color-default-border)' }}
      onSubmit={(event) => {
        event.preventDefault();
        void gerar();
      }}
    >
      <Stack gap="sm">
        <Title order={3} id="gerar-segredo" fz="sm" fw={600}>
          Gerar novo segredo
        </Title>
        {temVivo && <Text size="sm">{ATUAL_VALE_ATE_7_DIAS}</Text>}
        {recusado && <ActionDenied maw={560} />}
        {senha.aviso && (
          <Alert color={senha.aviso.cor} role="alert" maw={560}>
            {senha.aviso.texto}
          </Alert>
        )}
        {erro && (
          <Alert color="red" role="alert" maw={560}>
            {erro}
          </Alert>
        )}
        <CallerPasswordField {...senha.campo} description={DICA_SENHA_GERAR} />
        <Group gap="sm">
          <Button type="submit" size="sm" loading={senha.enviando} disabled={senha.bloqueado}>
            Gerar segredo
          </Button>
          <Button variant="subtle" size="sm" onClick={onCancel} disabled={senha.enviando}>
            Cancelar
          </Button>
        </Group>
      </Stack>
    </Box>
  );
}
