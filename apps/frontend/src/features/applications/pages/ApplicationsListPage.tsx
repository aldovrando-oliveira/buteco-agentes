import { useState } from 'react';
import {
  Alert,
  Anchor,
  Badge,
  Box,
  Button,
  Group,
  Paper,
  SegmentedControl,
  Skeleton,
  Stack,
  Table,
  Text,
  TextInput,
  Title,
} from '@mantine/core';
import { useDebouncedValue } from '@mantine/hooks';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { Plus } from 'lucide-react';
import { Link } from 'react-router';
import { AccessDenied } from '../../../components/feedback/AccessDenied';
import { is403 } from '../../../utils/failureNotice';
import { listApplications } from '../api/applicationsApi';
import { applicationsListKey } from '../queryKeys';
import type { EstadoFiltro } from '../types';
import {
  LISTA_ERRO,
  LISTA_SUBTITULO,
  LISTA_VAZIA,
  LISTA_VAZIA_TEXTO,
  NUNCA_USADA,
  formatAgo,
  rodapeDaLista,
  semResultado,
} from '../applicationsTexts';

// A lista de aplicações externas (quadro `AplicacoesLista`, `entrega=m2`; spec
// `panel-external-applications`, "Lista"). A busca e o filtro vão ao servidor (`?busca=`,
// `?estado=`, Q6), a busca depois de 300 ms sem digitar, e a lista anterior fica na tela enquanto a
// nova carrega. "Vazia" e "sem resultado" são estados diferentes: o primeiro só sem filtro algum.

const ESPERA_DA_BUSCA_MS = 300;

export function ApplicationsListPage() {
  const [busca, setBusca] = useState('');
  const [estado, setEstado] = useState<EstadoFiltro>('todas');
  const [buscaEnviada] = useDebouncedValue(busca, ESPERA_DA_BUSCA_MS);
  const filtro = { busca: buscaEnviada, estado };
  const query = useQuery({
    queryKey: applicationsListKey(filtro),
    queryFn: () => listApplications(filtro),
    placeholderData: keepPreviousData,
  });
  // A linha da busca e do filtro só aparece depois da primeira resposta com dados, como nas listas
  // de agentes e de bases: nunca na primeira carga nem no erro dela (#272). Depois disso, ela fica
  // nas buscas seguintes, inclusive enquanto a nova carrega (o `keepPreviousData` mantém a lista
  // anterior) e se a nova falhar, porque a busca vai ao servidor e não pode sumir enquanto a pessoa
  // digita. O `isPending` sozinho não basta: o erro de uma busca seguinte também fica sem `data`.
  const [jaCarregou, setJaCarregou] = useState(false);
  if (query.isSuccess && !jaCarregou) setJaCarregou(true);

  if (query.isError && is403(query.error)) return <AccessDenied />;

  const filtrando = buscaEnviada.trim() !== '' || estado !== 'todas';
  const apps = query.data ?? [];
  // O estado `vazia`: nenhuma aplicação, sem busca e com Todas. Nele a linha dos controles some;
  // na busca sem resultado ela fica, para o Admin corrigir a busca.
  const vazia = query.isSuccess && apps.length === 0 && !filtrando;
  const mostrarControles = (jaCarregou || query.isSuccess) && !vazia;
  const limpar = () => {
    setBusca('');
    setEstado('todas');
  };

  return (
    <Stack>
      <Group justify="space-between" align="flex-start" wrap="wrap">
        <Stack gap={2}>
          <Title order={2}>Aplicações externas</Title>
          <Text size="sm" c="dimmed">
            {LISTA_SUBTITULO}
          </Text>
        </Stack>
        <Button component={Link} to="/applications/new" leftSection={<Plus size={16} />}>
          Nova aplicação
        </Button>
      </Group>

      {mostrarControles && (
        <Group gap="sm" wrap="nowrap">
          {/* O padrão das listas de agentes e de bases: a busca e o controle segmentado na mesma
              linha, sem rótulo visível ("Estado" é o nome acessível do controle), e a linha some
              na primeira carga, no erro dela e na lista vazia (quadro `AplicacoesLista`, versão 69;
              #270, #272). */}
          <TextInput
            type="search"
            aria-label="Buscar aplicação"
            placeholder="Buscar por nome ou client ID"
            value={busca}
            onChange={(event) => setBusca(event.currentTarget.value)}
            maw={340}
            style={{ flex: 1 }}
          />
          <SegmentedControl
            aria-label="Estado"
            size="xs"
            withItemsBorders={false}
            style={{ border: '1px solid var(--mantine-color-default-border)', flexShrink: 0 }}
            value={estado}
            onChange={(value) => setEstado(value as EstadoFiltro)}
            data={[
              { value: 'todas', label: 'Todas' },
              { value: 'ativas', label: 'Ativas' },
              { value: 'desativadas', label: 'Desativadas' },
            ]}
          />
        </Group>
      )}

      {query.isPending ? (
        <Paper withBorder radius="md" p="md" role="status" aria-label="Carregando aplicações">
          <Stack gap="sm">
            {[40, 70, 55, 65].map((w) => (
              <Skeleton key={w} h={12} w={`${w}%`} />
            ))}
          </Stack>
        </Paper>
      ) : query.isError ? (
        <Alert color="red" role="alert">
          <Group justify="space-between" wrap="wrap" gap="sm">
            <Text size="sm">{LISTA_ERRO}</Text>
            <Button variant="default" size="xs" onClick={() => void query.refetch()}>
              Tentar de novo
            </Button>
          </Group>
        </Alert>
      ) : vazia ? (
        <Paper withBorder radius="md" px="lg" py={40}>
          <Stack gap={8} align="center" ta="center">
            <Text fw={600} fz="md">
              {LISTA_VAZIA}
            </Text>
            <Text size="sm" c="dimmed">
              {LISTA_VAZIA_TEXTO}
            </Text>
            <Button component={Link} to="/applications/new" mt={8}>
              Nova aplicação
            </Button>
          </Stack>
        </Paper>
      ) : apps.length === 0 ? (
        <Paper withBorder radius="md" px="lg" py={28}>
          <Stack gap={8} align="center" ta="center">
            <Text size="sm">{semResultado(buscaEnviada, estado)}</Text>
            <Button variant="default" size="xs" onClick={limpar}>
              Limpar busca
            </Button>
          </Stack>
        </Paper>
      ) : (
        <>
          <Paper withBorder radius="md" style={{ overflowX: 'auto' }}>
            <Table miw={860} verticalSpacing="sm" horizontalSpacing="md">
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>Aplicação</Table.Th>
                  <Table.Th>Identidade</Table.Th>
                  <Table.Th>Agentes liberados</Table.Th>
                  <Table.Th>Estado</Table.Th>
                  <Table.Th>Último uso</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {apps.map((app) => (
                  <Table.Tr key={app.id}>
                    <Table.Td>
                      <Stack gap={0}>
                        <Anchor component={Link} to={`/applications/${app.id}`} fw={500} size="sm">
                          {app.nome}
                        </Anchor>
                        {app.descricao && (
                          <Text size="xs" c="dimmed">
                            {app.descricao}
                          </Text>
                        )}
                      </Stack>
                    </Table.Td>
                    <Table.Td>
                      <Stack gap={0}>
                        <Text ff="monospace" size="xs">
                          {app.clientId}
                        </Text>
                        <Text size="xs" c="dimmed">
                          Credencial do Buteco
                        </Text>
                      </Stack>
                    </Table.Td>
                    <Table.Td>
                      {app.agentes.length === 0 ? (
                        <Text size="sm" c="dimmed">
                          Nenhum
                        </Text>
                      ) : (
                        <Group gap={6}>
                          {app.agentes.map((agente) => (
                            <Badge
                              key={agente.id}
                              variant="outline"
                              color="gray"
                              radius="xl"
                              tt="none"
                              fw={400}
                            >
                              {agente.nome}
                            </Badge>
                          ))}
                        </Group>
                      )}
                    </Table.Td>
                    <Table.Td>
                      <Badge color={app.ativa ? 'green' : 'gray'} variant="light">
                        {app.ativa ? 'Ativa' : 'Desativada'}
                      </Badge>
                    </Table.Td>
                    <Table.Td>
                      <Text
                        size="sm"
                        c={
                          app.ultimoUso
                            ? app.ativa
                              ? undefined
                              : 'dimmed'
                            : app.ativa
                              ? 'yellow.8'
                              : 'dimmed'
                        }
                      >
                        {app.ultimoUso ? formatAgo(app.ultimoUso) : NUNCA_USADA}
                      </Text>
                    </Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          </Paper>
          <Box>
            <Text size="xs" c="dimmed">
              {rodapeDaLista(apps.length)}
            </Text>
          </Box>
        </>
      )}
    </Stack>
  );
}
