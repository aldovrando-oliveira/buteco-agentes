import { useState } from 'react';
import {
  Alert,
  Anchor,
  Badge,
  Button,
  Center,
  Group,
  Loader,
  Paper,
  Stack,
  Tabs,
  Text,
} from '@mantine/core';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useParams, useSearchParams } from 'react-router';
import { DetailHeader } from '../../../components/layout/DetailHeader';
import { AccessDenied } from '../../../components/feedback/AccessDenied';
import { ActionDenied } from '../../../components/feedback/ActionDenied';
import {
  failureNotice,
  is403,
  isRoleRefusal,
  type FailureNotice,
} from '../../../utils/failureNotice';
import { formatDate } from '../../../utils/panelDates';
import { ConfirmBar } from '../../users/components/ConfirmBar';
import { detailRetry, is404 } from '../../users/detailRetry';
import {
  activateApplication,
  deactivateApplication,
  getApplication,
  listApplicationAgents,
} from '../api/applicationsApi';
import { APPLICATIONS_KEY, applicationAgentsKey, applicationKey } from '../queryKeys';
import type { CreatedSecret } from '../types';
import { ApplicationCredentialTab } from '../components/ApplicationCredentialTab';
import { ApplicationAgentsTab } from '../components/ApplicationAgentsTab';
import {
  APLICACAO_NAO_ENCONTRADA,
  DETALHE_ERRO,
  REATIVADA,
  confirmarDesativar,
} from '../applicationsTexts';

// O detalhe da aplicação (quadro `AplicacaoDetalhe`, versão 66; spec `panel-external-applications`,
// "Detalhe da aplicação" e "Desativar e reativar"). A aba vive em `?tab=credencial`; sem `tab`, é
// "Agentes" (D1). O segredo gerado na aba Credencial fica AQUI, no estado da página, para continuar
// na tela ao trocar de aba (D3); nunca em consulta, `state` de navegação ou armazenamento.

const ABA_CREDENCIAL = 'credencial';
const ABA_AGENTES = 'agentes';

type Desativacao = { enviando: boolean; falha: FailureNotice | null; recusada: boolean } | null;

export function ApplicationDetailPage() {
  const { id = '' } = useParams();
  const queryClient = useQueryClient();
  const [searchParams, setSearchParams] = useSearchParams();
  const aba = searchParams.get('tab') === ABA_CREDENCIAL ? ABA_CREDENCIAL : ABA_AGENTES;
  const detalhe = useQuery({
    queryKey: applicationKey(id),
    queryFn: () => getApplication(id),
    retry: detailRetry(queryClient),
  });
  const agentes = useQuery({
    queryKey: applicationAgentsKey(id),
    queryFn: () => listApplicationAgents(id),
    enabled: detalhe.isSuccess,
  });
  const [segredoNovo, setSegredoNovo] = useState<CreatedSecret | null>(null);
  const [desativacao, setDesativacao] = useState<Desativacao>(null);
  const [reativada, setReativada] = useState(false);
  const [falhaReativar, setFalhaReativar] = useState<FailureNotice | null>(null);
  const [reativando, setReativando] = useState(false);

  const trocarAba = (proxima: string | null) => {
    const next = new URLSearchParams(searchParams);
    if (proxima === ABA_CREDENCIAL) next.set('tab', ABA_CREDENCIAL);
    else next.delete('tab');
    setSearchParams(next, { replace: true });
  };

  const reler = () => queryClient.invalidateQueries({ queryKey: APPLICATIONS_KEY });

  if (detalhe.isPending) {
    return (
      <Center py="xl" role="status">
        <Group gap="xs">
          <Loader size="sm" />
          <Text size="sm" c="dimmed">
            Carregando a aplicação…
          </Text>
        </Group>
      </Center>
    );
  }

  if (detalhe.isError) {
    if (is403(detalhe.error)) return <AccessDenied />;
    if (is404(detalhe.error)) {
      return (
        <Center py={60}>
          <Paper withBorder radius="md" p="xl" maw={460}>
            <Stack gap="sm" align="center" ta="center">
              <Text fw={600}>{APLICACAO_NAO_ENCONTRADA}</Text>
              <Anchor component={Link} to="/applications" size="sm">
                Voltar para Aplicações externas
              </Anchor>
            </Stack>
          </Paper>
        </Center>
      );
    }
    return (
      <Alert color="red" role="alert">
        <Group justify="space-between" gap="sm" wrap="wrap">
          <Text size="sm">{DETALHE_ERRO}</Text>
          <Button variant="default" size="xs" onClick={() => void detalhe.refetch()}>
            Tentar de novo
          </Button>
        </Group>
      </Alert>
    );
  }

  const app = detalhe.data;

  const desativar = async () => {
    setDesativacao({ enviando: true, falha: null, recusada: false });
    try {
      await deactivateApplication(app.id);
      setDesativacao(null);
      setReativada(false);
      await reler();
    } catch (error) {
      setDesativacao({
        enviando: false,
        falha: isRoleRefusal(error) ? null : failureNotice(error),
        recusada: isRoleRefusal(error),
      });
    }
  };

  const reativar = async () => {
    setFalhaReativar(null);
    setReativando(true);
    try {
      await activateApplication(app.id);
      setReativada(true);
      await reler();
    } catch (error) {
      setFalhaReativar(failureNotice(error));
    } finally {
      setReativando(false);
    }
  };

  return (
    <Stack>
      <DetailHeader
        backTo="/applications"
        backLabel="Aplicações externas"
        title={app.nome}
        status={
          <Badge color={app.ativa ? 'green' : 'gray'} variant="light">
            {app.ativa ? 'Ativa' : 'Desativada'}
          </Badge>
        }
        description={
          [
            app.descricao,
            !app.ativa && app.desativadaEm ? `Desativada em ${formatDate(app.desativadaEm)}` : null,
          ]
            .filter(Boolean)
            .join(' · ') || undefined
        }
        showDescription={Boolean(app.descricao) || !app.ativa}
        actions={
          <>
            <Button variant="default" component={Link} to={`/applications/${app.id}/edit`}>
              Editar
            </Button>
            {app.ativa ? (
              <Button
                color="red"
                variant="light"
                onClick={() => setDesativacao({ enviando: false, falha: null, recusada: false })}
                disabled={desativacao !== null}
              >
                Desativar
              </Button>
            ) : (
              <Button onClick={() => void reativar()} loading={reativando}>
                Reativar
              </Button>
            )}
          </>
        }
      />

      {desativacao && (
        <Stack gap="xs">
          <ConfirmBar
            solta
            texto={confirmarDesativar(app.nome)}
            confirmLabel="Desativar"
            onConfirm={() => void desativar()}
            onCancel={() => setDesativacao(null)}
            enviando={desativacao.enviando}
            falha={desativacao.falha}
          />
          {desativacao.recusada && <ActionDenied />}
        </Stack>
      )}
      {reativada && app.ativa && (
        <Alert color="blue" role="status">
          <Text size="sm">{REATIVADA}</Text>
        </Alert>
      )}
      {falhaReativar && (
        <Alert color={falhaReativar.cor} role="alert">
          <Text size="sm">{falhaReativar.texto}</Text>
        </Alert>
      )}

      <Tabs value={aba} onChange={trocarAba} keepMounted={false}>
        <Tabs.List>
          <Tabs.Tab
            value={ABA_AGENTES}
            rightSection={
              // O contador vem da lista dos agentes liberados: o detalhe não traz `agentes`.
              agentes.data && (
                <Badge size="xs" variant="light" color="gray" circle>
                  {agentes.data.length}
                </Badge>
              )
            }
          >
            Agentes
          </Tabs.Tab>
          <Tabs.Tab value={ABA_CREDENCIAL}>Credencial e conexão</Tabs.Tab>
        </Tabs.List>
        <Tabs.Panel value={ABA_AGENTES} pt="md">
          <ApplicationAgentsTab app={app} agentes={agentes} />
        </Tabs.Panel>
        <Tabs.Panel value={ABA_CREDENCIAL} pt="md">
          <ApplicationCredentialTab
            app={app}
            segredoNovo={segredoNovo}
            onSegredoNovo={setSegredoNovo}
          />
        </Tabs.Panel>
      </Tabs>
    </Stack>
  );
}
