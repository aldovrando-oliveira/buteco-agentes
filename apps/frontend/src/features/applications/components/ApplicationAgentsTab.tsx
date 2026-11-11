import { Fragment, useState } from 'react';
import {
  Alert,
  Anchor,
  Badge,
  Box,
  Button,
  Checkbox,
  Group,
  NativeSelect,
  Skeleton,
  Stack,
  Table,
  Text,
  Title,
} from '@mantine/core';
import { useQuery, useQueryClient, type UseQueryResult } from '@tanstack/react-query';
import { Check } from 'lucide-react';
import { Link } from 'react-router';
import { SectionedCard } from '../../../components/data/SectionedCard';
import { ActionDenied } from '../../../components/feedback/ActionDenied';
import {
  failureNotice,
  is403,
  isApiFailure,
  isRoleRefusal,
  type FailureNotice,
} from '../../../utils/failureNotice';
import { ConfirmBar } from '../../users/components/ConfirmBar';
import { listAgents } from '../../agents/api/agentsApi';
import {
  fieldErrors,
  grantAgent,
  revokeAgent,
  updateAgentOperations,
} from '../api/applicationsApi';
import { APPLICATIONS_KEY, applicationAgentsKey } from '../queryKeys';
import { OPERACOES, type ApplicationAgent, type ApplicationDetail, type Operacao } from '../types';
import {
  AGENTES_ERRO,
  AGENTES_VAZIO,
  AGENTE_INATIVO_AVISO,
  AGENTE_NAO_ENCONTRADO,
  AVISO_DESATIVADA_AGENTES,
  DELEGACAO,
  DESCRICAO_OPERACAO,
  MARQUE_UMA,
  MARQUE_UMA_LINHA,
  ROTULO_OPERACAO,
  TODOS_LIBERADOS,
  VINCULO_RETIRADO,
  confirmarRemover,
} from '../applicationsTexts';

// A aba "Agentes" (quadros `AplicacaoDetalhe`, `AplicacaoDetalheAdicionar` e
// `AplicacaoDetalheEditar`, versão 66; spec `panel-external-application-agents`). Sem modal:
// adicionar acontece dentro do cartão, editar na linha, remover com a confirmação na linha, e uma
// ação por vez (design.md da change aplicacoes-externas-painel, D5).

const PADRAO_DA_ADICAO: Operacao[] = ['enviar-mensagem', 'consultar-task'];

/** As operações marcadas, na ordem da tabela do vínculo (o servidor guarda nessa ordem). */
const naOrdem = (marcadas: ReadonlySet<Operacao>) => OPERACOES.filter((op) => marcadas.has(op));

type Modo =
  | { tipo: 'nenhum' }
  | { tipo: 'adicionar' }
  | {
      tipo: 'editar';
      agenteId: string;
      marcadas: Set<Operacao>;
      enviando: boolean;
      falha: FailureNotice | null;
    }
  | { tipo: 'remover'; agenteId: string; enviando: boolean; falha: FailureNotice | null };

interface ApplicationAgentsTabProps {
  app: ApplicationDetail;
  agentes: UseQueryResult<ApplicationAgent[]>;
}

export function ApplicationAgentsTab({ app, agentes }: ApplicationAgentsTabProps) {
  const queryClient = useQueryClient();
  const [modo, setModo] = useState<Modo>({ tipo: 'nenhum' });
  const [retirado, setRetirado] = useState(false);
  const [recusado, setRecusado] = useState(false);
  const lista = agentes.data ?? [];

  const reler = () => queryClient.invalidateQueries({ queryKey: APPLICATIONS_KEY });
  const abrir = (proximo: Modo) => {
    setRetirado(false);
    setRecusado(false);
    setModo(proximo);
  };

  const salvar = async (agenteId: string, marcadas: Set<Operacao>) => {
    setModo({ tipo: 'editar', agenteId, marcadas, enviando: true, falha: null });
    try {
      await updateAgentOperations(app.id, agenteId, naOrdem(marcadas));
      setModo({ tipo: 'nenhum' });
    } catch (error) {
      if (isApiFailure(error) && error.status === 404) {
        // Outro Admin retirou o vínculo entre abrir e salvar (#187, "Retirado entre abrir e salvar").
        setModo({ tipo: 'nenhum' });
        setRetirado(true);
      } else if (isRoleRefusal(error)) {
        setModo({ tipo: 'nenhum' });
        setRecusado(true);
        return;
      } else {
        setModo({
          tipo: 'editar',
          agenteId,
          marcadas,
          enviando: false,
          falha: failureNotice(error),
        });
        return;
      }
    }
    await reler();
  };

  const remover = async (agenteId: string) => {
    setModo({ tipo: 'remover', agenteId, enviando: true, falha: null });
    try {
      await revokeAgent(app.id, agenteId);
    } catch (error) {
      // O `404` é sucesso: o vínculo pedido já não existe (Q9). O resto aparece na faixa.
      if (!(isApiFailure(error) && error.status === 404)) {
        if (isRoleRefusal(error)) {
          setModo({ tipo: 'nenhum' });
          setRecusado(true);
          return;
        }
        setModo({ tipo: 'remover', agenteId, enviando: false, falha: failureNotice(error) });
        return;
      }
    }
    setModo({ tipo: 'nenhum' });
    await reler();
  };

  const ocupado = modo.tipo !== 'nenhum';

  return (
    <SectionedCard
      title="Agentes liberados"
      action={
        modo.tipo !== 'adicionar' && (
          <Button
            size="xs"
            onClick={() => abrir({ tipo: 'adicionar' })}
            disabled={ocupado || !agentes.isSuccess}
          >
            Adicionar agente
          </Button>
        )
      }
    >
      {!app.ativa && (
        <Box px="md" pt="sm">
          <Alert color="gray" role="status" p="sm">
            <Text size="sm">{AVISO_DESATIVADA_AGENTES}</Text>
          </Alert>
        </Box>
      )}
      {retirado && (
        <Box px="md" pt="sm">
          <Alert color="red" role="alert" p="sm">
            <Text size="sm">{VINCULO_RETIRADO}</Text>
          </Alert>
        </Box>
      )}
      {recusado && (
        <Box px="md" pt="sm">
          <ActionDenied />
        </Box>
      )}

      {modo.tipo === 'adicionar' && (
        <AddAgentForm
          app={app}
          liberados={lista}
          onCancel={() => abrir({ tipo: 'nenhum' })}
          onAdicionado={() => {
            setModo({ tipo: 'nenhum' });
            void reler();
          }}
          onRecusado={() => {
            setModo({ tipo: 'nenhum' });
            setRecusado(true);
          }}
        />
      )}

      {agentes.isPending ? (
        <Stack gap="sm" p="md" role="status" aria-label="Carregando os agentes">
          {[55, 70, 45].map((w) => (
            <Skeleton key={w} h={12} w={`${w}%`} />
          ))}
        </Stack>
      ) : agentes.isError ? (
        <Box p="md">
          {is403(agentes.error) ? (
            <ActionDenied />
          ) : (
            <Alert color="red" role="alert">
              <Group justify="space-between" gap="sm" wrap="wrap">
                <Text size="sm">{AGENTES_ERRO}</Text>
                <Button variant="default" size="xs" onClick={() => void agentes.refetch()}>
                  Tentar de novo
                </Button>
              </Group>
            </Alert>
          )}
        </Box>
      ) : lista.length === 0 ? (
        modo.tipo !== 'adicionar' && (
          <SectionedCard.Body>
            <Text size="sm" c="dimmed" py={6}>
              {AGENTES_VAZIO}
            </Text>
          </SectionedCard.Body>
        )
      ) : (
        <>
          <Box style={{ overflowX: 'auto' }}>
            <Table miw={760} verticalSpacing="sm" horizontalSpacing="md">
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>Agente</Table.Th>
                  {OPERACOES.map((op) => (
                    <Table.Th key={op} ta="center">
                      {ROTULO_OPERACAO[op]}
                    </Table.Th>
                  ))}
                  <Table.Th>Ações</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {lista.map((agente) => {
                  const editando =
                    modo.tipo === 'editar' && modo.agenteId === agente.agenteId ? modo : null;
                  const removendo =
                    modo.tipo === 'remover' && modo.agenteId === agente.agenteId ? modo : null;
                  const marcadas = editando ? editando.marcadas : new Set(agente.operacoes);
                  const semOperacao = editando !== null && marcadas.size === 0;
                  return (
                    <Fragment key={agente.agenteId}>
                      <Table.Tr bg={editando ? 'var(--mantine-primary-color-light)' : undefined}>
                        <Table.Td>
                          <Stack gap={0}>
                            <Group gap={6}>
                              <Anchor
                                component={Link}
                                to={`/agents/${agente.agenteId}`}
                                fw={500}
                                size="sm"
                              >
                                {agente.nome}
                              </Anchor>
                              {!agente.ativo && (
                                <Badge color="gray" variant="light" size="sm">
                                  Inativo
                                </Badge>
                              )}
                            </Group>
                            {semOperacao ? (
                              <Text size="xs" c="red">
                                {MARQUE_UMA_LINHA}
                              </Text>
                            ) : (
                              agente.descricao && (
                                <Text size="xs" c="dimmed">
                                  {agente.descricao}
                                </Text>
                              )
                            )}
                          </Stack>
                        </Table.Td>
                        {OPERACOES.map((op) => (
                          <Table.Td key={op} ta="center">
                            {editando ? (
                              <Checkbox
                                display="inline-block"
                                aria-label={`${ROTULO_OPERACAO[op]} em ${agente.nome}`}
                                checked={marcadas.has(op)}
                                disabled={editando.enviando}
                                onChange={(event) => {
                                  const proximas = new Set(marcadas);
                                  if (event.currentTarget.checked) proximas.add(op);
                                  else proximas.delete(op);
                                  setModo({ ...editando, marcadas: proximas, falha: null });
                                }}
                              />
                            ) : marcadas.has(op) ? (
                              <>
                                <Check size={16} aria-hidden="true" />
                                <Text span style={visuallyHidden}>
                                  Permitido
                                </Text>
                              </>
                            ) : (
                              <>
                                <Text span aria-hidden="true" c="dimmed">
                                  —
                                </Text>
                                <Text span style={visuallyHidden}>
                                  Não permitido
                                </Text>
                              </>
                            )}
                          </Table.Td>
                        ))}
                        <Table.Td>
                          {editando ? (
                            <Group gap="xs" wrap="nowrap">
                              <Button
                                size="compact-sm"
                                disabled={semOperacao}
                                loading={editando.enviando}
                                onClick={() => void salvar(agente.agenteId, editando.marcadas)}
                              >
                                Salvar
                              </Button>
                              <Button
                                size="compact-sm"
                                variant="subtle"
                                disabled={editando.enviando}
                                onClick={() => abrir({ tipo: 'nenhum' })}
                              >
                                Cancelar
                              </Button>
                            </Group>
                          ) : (
                            <Group gap="xs" wrap="nowrap">
                              <Button
                                size="compact-sm"
                                variant="default"
                                disabled={ocupado}
                                aria-label={`Editar operações de ${agente.nome}`}
                                onClick={() =>
                                  abrir({
                                    tipo: 'editar',
                                    agenteId: agente.agenteId,
                                    marcadas: new Set(agente.operacoes),
                                    enviando: false,
                                    falha: null,
                                  })
                                }
                              >
                                Editar
                              </Button>
                              <Button
                                size="compact-sm"
                                variant="subtle"
                                color="red"
                                disabled={ocupado}
                                aria-label={`Remover ${agente.nome}`}
                                onClick={() =>
                                  abrir({
                                    tipo: 'remover',
                                    agenteId: agente.agenteId,
                                    enviando: false,
                                    falha: null,
                                  })
                                }
                              >
                                Remover
                              </Button>
                            </Group>
                          )}
                        </Table.Td>
                      </Table.Tr>
                      {editando?.falha && (
                        <Table.Tr>
                          <Table.Td colSpan={5}>
                            <Alert color={editando.falha.cor} role="alert" p="xs">
                              <Text size="sm">{editando.falha.texto}</Text>
                            </Alert>
                          </Table.Td>
                        </Table.Tr>
                      )}
                      {removendo && (
                        <Table.Tr>
                          <Table.Td colSpan={5} p={0}>
                            <ConfirmBar
                              texto={confirmarRemover(agente.nome)}
                              confirmLabel="Remover"
                              onConfirm={() => void remover(agente.agenteId)}
                              onCancel={() => abrir({ tipo: 'nenhum' })}
                              enviando={removendo.enviando}
                              falha={removendo.falha}
                            />
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
              {DELEGACAO}
            </Text>
          </SectionedCard.Body>
        </>
      )}
    </SectionedCard>
  );
}

const visuallyHidden = {
  position: 'absolute',
  width: 1,
  height: 1,
  overflow: 'hidden',
  clip: 'rect(0 0 0 0)',
  whiteSpace: 'nowrap',
} as const;

interface AddAgentFormProps {
  app: ApplicationDetail;
  liberados: ApplicationAgent[];
  onCancel: () => void;
  onAdicionado: () => void;
  onRecusado: () => void;
}

/** "Novo agente para esta aplicação", dentro do cartão e acima da grade. */
function AddAgentForm({ app, liberados, onCancel, onAdicionado, onRecusado }: AddAgentFormProps) {
  const queryClient = useQueryClient();
  const catalogo = useQuery({ queryKey: ['agents'], queryFn: listAgents });
  const [agenteId, setAgenteId] = useState('');
  const [marcadas, setMarcadas] = useState<Set<Operacao>>(new Set(PADRAO_DA_ADICAO));
  const [erroAgente, setErroAgente] = useState<string | null>(null);
  const [erroForm, setErroForm] = useState<string | null>(null);
  const [aviso, setAviso] = useState<FailureNotice | null>(null);
  const [enviando, setEnviando] = useState(false);

  const usados = new Set(liberados.map((l) => l.agenteId));
  const disponiveis = (catalogo.data ?? []).filter((a) => !usados.has(a.id));
  const escolhido = disponiveis.find((a) => a.id === agenteId) ?? null;
  const semOperacao = marcadas.size === 0;

  const adicionar = async () => {
    if (!escolhido || semOperacao) return;
    setErroAgente(null);
    setErroForm(null);
    setAviso(null);
    setEnviando(true);
    try {
      await grantAgent(app.id, escolhido.id, naOrdem(marcadas));
      onAdicionado();
    } catch (error) {
      if (isRoleRefusal(error)) {
        onRecusado();
      } else if (isApiFailure(error) && error.status === 409) {
        // Outro Admin liberou o mesmo agente: o `title` do servidor (divergência 1, aceita).
        setErroForm(error.title ?? null);
        setAgenteId('');
        void queryClient.invalidateQueries({ queryKey: applicationAgentsKey(app.id) });
      } else if (isApiFailure(error) && error.status === 400) {
        const campos = fieldErrors(error);
        if (campos.agenteId) {
          setErroAgente(campos.agenteId ?? AGENTE_NAO_ENCONTRADO);
          setAgenteId('');
          void catalogo.refetch();
        } else {
          setErroForm(campos.operacoes ?? null);
        }
      } else {
        setAviso(failureNotice(error));
      }
    } finally {
      setEnviando(false);
    }
  };

  return (
    <Box
      component="form"
      noValidate
      aria-labelledby="novo-agente"
      px="md"
      py="md"
      bg="var(--mantine-primary-color-light)"
      style={{ borderBottom: '1px solid var(--mantine-color-default-border)' }}
      onSubmit={(event) => {
        event.preventDefault();
        void adicionar();
      }}
    >
      <Stack gap="sm" maw={720}>
        <Title order={3} id="novo-agente" fz="sm" fw={600}>
          Novo agente para esta aplicação
        </Title>
        {catalogo.isSuccess && disponiveis.length === 0 ? (
          <Stack gap={4}>
            <Text size="sm" fw={500}>
              Agente
            </Text>
            <Text size="sm" c="dimmed">
              {TODOS_LIBERADOS}
            </Text>
          </Stack>
        ) : (
          <NativeSelect
            label="Agente"
            value={agenteId}
            disabled={!catalogo.isSuccess}
            onChange={(event) => {
              setAgenteId(event.currentTarget.value);
              setErroAgente(null);
            }}
            data={[
              {
                value: '',
                label: catalogo.isError
                  ? 'Não foi possível carregar os agentes.'
                  : 'Selecione um agente…',
              },
              ...disponiveis.map((a) => ({
                value: a.id,
                label: a.isActive ? a.name : `${a.name} (inativo)`,
              })),
            ]}
            error={erroAgente}
            description={escolhido?.description ?? undefined}
            inputWrapperOrder={['label', 'input', 'error', 'description']}
            maw={420}
          />
        )}
        <Checkbox.Group
          label="O que a aplicação pode fazer com este agente"
          value={naOrdem(marcadas)}
          onChange={(valores) => setMarcadas(new Set(valores as Operacao[]))}
        >
          <Group gap="sm" mt={6}>
            {OPERACOES.map((op) => (
              <Checkbox
                key={op}
                value={op}
                label={ROTULO_OPERACAO[op]}
                description={DESCRICAO_OPERACAO[op]}
              />
            ))}
          </Group>
        </Checkbox.Group>
        {escolhido && !escolhido.isActive && (
          <Alert color="yellow" p="xs">
            <Text size="sm">{AGENTE_INATIVO_AVISO}</Text>
          </Alert>
        )}
        {erroForm && (
          <Alert color="red" role="alert" p="xs">
            <Text size="sm">{erroForm}</Text>
          </Alert>
        )}
        {aviso && (
          <Alert color={aviso.cor} role="alert" p="xs">
            <Text size="sm">{aviso.texto}</Text>
          </Alert>
        )}
        <Group gap="sm">
          <Button type="submit" size="sm" disabled={!escolhido || semOperacao} loading={enviando}>
            Adicionar
          </Button>
          <Button variant="subtle" size="sm" onClick={onCancel} disabled={enviando}>
            Cancelar
          </Button>
          {semOperacao && (
            <Text size="sm" c="red">
              {MARQUE_UMA}
            </Text>
          )}
        </Group>
      </Stack>
    </Box>
  );
}
