import { useState } from 'react';
import { Alert, Box, Button, Center, Group, Loader, Stack, Text, Title } from '@mantine/core';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate, useParams } from 'react-router';
import { BackLink } from '../../../components/layout/BackLink';
import { AccessDenied } from '../../../components/feedback/AccessDenied';
import { ActionDenied } from '../../../components/feedback/ActionDenied';
import {
  failureNotice,
  is403,
  isApiFailure,
  isRoleRefusal,
  type FailureNotice,
} from '../../../utils/failureNotice';
import { detailRetry, is404 } from '../../users/detailRetry';
import { fieldErrors, getApplication, updateApplication } from '../api/applicationsApi';
import { APPLICATIONS_KEY, applicationKey } from '../queryKeys';
import type { ApplicationDetail } from '../types';
import { ApplicationIdentificationCard } from '../components/ApplicationForm';
import {
  conferirCampos,
  corpoDosCampos,
  type CamposAplicacao,
  type ErrosAplicacao,
} from '../applicationFields';
import {
  APLICACAO_NAO_ENCONTRADA,
  DETALHE_ERRO,
  EDITAR_NAO_MUDA_CREDENCIAL,
  NOME_REPETIDO,
} from '../applicationsTexts';

// Editar aplicação (quadro `AplicacaoEditar`, versão 66; spec `panel-external-applications`,
// "Editar nome e descrição"). Sem "Credencial" e sem senha: editar não gera credencial (#186, D4).

export function ApplicationEditPage() {
  const { id = '' } = useParams();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const detalhe = useQuery({
    queryKey: applicationKey(id),
    queryFn: () => getApplication(id),
    retry: detailRetry(queryClient),
  });

  if (detalhe.isError) {
    if (is403(detalhe.error)) return <AccessDenied />;
    return (
      <Alert color="red" role="alert">
        <Text size="sm">{is404(detalhe.error) ? APLICACAO_NAO_ENCONTRADA : DETALHE_ERRO}</Text>
      </Alert>
    );
  }
  if (!detalhe.data) {
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

  // O formulário monta com o detalhe lido e guarda o que se digita: a releitura em segundo plano
  // não o apaga.
  return (
    <EditForm app={detalhe.data} onSalvo={() => navigate(`/applications/${detalhe.data.id}`)} />
  );
}

function EditForm({ app, onSalvo }: { app: ApplicationDetail; onSalvo: () => void }) {
  const queryClient = useQueryClient();
  const [campos, setCampos] = useState<CamposAplicacao>({
    nome: app.nome,
    descricao: app.descricao ?? '',
  });
  const [erros, setErros] = useState<ErrosAplicacao>({});
  const [aviso, setAviso] = useState<FailureNotice | null>(null);
  const [recusado, setRecusado] = useState(false);
  const [salvando, setSalvando] = useState(false);

  const salvar = async () => {
    setAviso(null);
    setRecusado(false);
    const locais = conferirCampos(campos);
    setErros(locais);
    if (locais.nome || locais.descricao) return;
    setSalvando(true);
    try {
      await updateApplication(app.id, corpoDosCampos(campos));
      await queryClient.invalidateQueries({ queryKey: APPLICATIONS_KEY });
      onSalvo();
    } catch (error) {
      if (isRoleRefusal(error)) setRecusado(true);
      else if (isApiFailure(error) && error.status === 409)
        setErros({ nome: error.title ?? NOME_REPETIDO });
      else if (isApiFailure(error) && error.status === 400) {
        const campo = fieldErrors(error);
        setErros({ nome: campo.nome, descricao: campo.descricao });
      } else setAviso(failureNotice(error));
    } finally {
      setSalvando(false);
    }
  };

  return (
    <Stack maw={880}>
      <Stack gap={4}>
        <BackLink to={`/applications/${app.id}`} label={app.nome} />
        <Title order={2}>Editar aplicação</Title>
      </Stack>
      <Box
        component="form"
        noValidate
        onSubmit={(event) => {
          event.preventDefault();
          void salvar();
        }}
      >
        <Stack>
          <ApplicationIdentificationCard campos={campos} erros={erros} onChange={setCampos} />
          <Text size="sm" c="dimmed">
            {EDITAR_NAO_MUDA_CREDENCIAL}
          </Text>
          {recusado && <ActionDenied maw={560} />}
          {aviso && (
            <Alert color={aviso.cor} role="alert" maw={560}>
              {aviso.texto}
            </Alert>
          )}
          <Group justify="flex-end" gap="sm">
            <Button variant="default" component={Link} to={`/applications/${app.id}`}>
              Cancelar
            </Button>
            <Button type="submit" disabled={salvando}>
              {salvando ? 'Salvando…' : 'Salvar'}
            </Button>
          </Group>
        </Stack>
      </Box>
    </Stack>
  );
}
