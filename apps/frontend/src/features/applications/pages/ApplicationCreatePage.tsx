import { useState } from 'react';
import { Alert, Box, Button, Group, Stack, Text, Title } from '@mantine/core';
import { useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate } from 'react-router';
import { SectionedCard } from '../../../components/data/SectionedCard';
import { BackLink } from '../../../components/layout/BackLink';
import { ActionDenied } from '../../../components/feedback/ActionDenied';
import { isApiFailure, isRoleRefusal } from '../../../utils/failureNotice';
import { useCallerPassword } from '../../users/callerPassword';
import { CallerPasswordField } from '../../users/components/CallerPasswordField';
import { createApplication, fieldErrors } from '../api/applicationsApi';
import { APPLICATIONS_KEY } from '../queryKeys';
import type { CreatedApplication } from '../types';
import { ApplicationIdentificationCard } from '../components/ApplicationForm';
import {
  conferirCampos,
  corpoDosCampos,
  type CamposAplicacao,
  type ErrosAplicacao,
} from '../applicationFields';
import { CreatedCredentialCard } from '../components/CreatedCredentialCard';
import {
  DICA_SENHA_CRIAR,
  ENDPOINT_DEPOIS_DE_CRIAR,
  NASCE_SEM_AGENTES_ANTES,
  NASCE_SEM_AGENTES_DEPOIS,
  NASCE_SEM_AGENTES_DESTAQUE,
  NOME_REPETIDO,
} from '../applicationsTexts';

// Nova aplicação (quadro `AplicacaoNova`, `federadoDisponivel=false`, versão 66; spec
// `panel-external-applications`, "Nova aplicação" e "Credencial criada").
//
// A RESPOSTA DO `201` TRAZ O SEGREDO. Por isso a chamada é direta, pelo `useCallerPassword`, e não
// por `useMutation` (o `MutationCache` guardaria o `data` até o `gcTime`); a resposta não vai a
// `setQueryData`; e a navegação para o detalhe não leva `state`. O segredo vive só em `criada`,
// neste componente, e some ao desmontar (design.md da change aplicacoes-externas-painel, D3).

export function ApplicationCreatePage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [campos, setCampos] = useState<CamposAplicacao>({ nome: '', descricao: '' });
  const [erros, setErros] = useState<ErrosAplicacao>({});
  const [recusado, setRecusado] = useState(false);
  const [criada, setCriada] = useState<CreatedApplication | null>(null);
  const senha = useCallerPassword();

  const criar = async () => {
    setRecusado(false);
    const locais = conferirCampos(campos);
    setErros(locais);
    if (locais.nome || locais.descricao) return;
    const result = await senha.enviar(
      (c) => createApplication({ ...corpoDosCampos(campos), ...c }),
      (error) => {
        if (!isApiFailure(error)) return false;
        if (isRoleRefusal(error)) {
          setRecusado(true);
          return true;
        }
        if (error.status === 409) {
          setErros({ nome: error.title ?? NOME_REPETIDO });
          return true;
        }
        if (error.status === 400) {
          const campo = fieldErrors(error);
          setErros({ nome: campo.nome, descricao: campo.descricao });
          return true;
        }
        return false;
      },
    );
    if (!result) return;
    setCriada(result);
    void queryClient.invalidateQueries({ queryKey: APPLICATIONS_KEY });
  };

  return (
    <Stack maw={880}>
      <Stack gap={4}>
        <BackLink to="/applications" label="Aplicações externas" />
        <Title order={2}>Nova aplicação externa</Title>
      </Stack>

      {criada ? (
        <CreatedCredentialCard
          criada={criada}
          onIr={() => navigate(`/applications/${criada.id}`)}
        />
      ) : (
        <Box
          component="form"
          noValidate
          onSubmit={(event) => {
            event.preventDefault();
            void criar();
          }}
        >
          <Stack>
            <ApplicationIdentificationCard campos={campos} erros={erros} onChange={setCampos} />

            <SectionedCard title="Credencial">
              <SectionedCard.Body>
                <Stack gap={4} py={6}>
                  <Text size="sm">
                    O Buteco vai gerar um <b>client ID</b> e um <b>segredo</b> para esta aplicação.
                    Eles aparecem uma única vez, logo depois de criar; o Buteco guarda só o hash do
                    segredo.
                  </Text>
                  <Text size="xs" c="dimmed">
                    {ENDPOINT_DEPOIS_DE_CRIAR}
                  </Text>
                </Stack>
              </SectionedCard.Body>
            </SectionedCard>

            <SectionedCard title="Confirmação">
              <SectionedCard.Body>
                <Stack gap="sm" py={6}>
                  {recusado && <ActionDenied maw={560} />}
                  {senha.aviso && (
                    <Alert color={senha.aviso.cor} role="alert" maw={560}>
                      {senha.aviso.texto}
                    </Alert>
                  )}
                  <CallerPasswordField {...senha.campo} description={DICA_SENHA_CRIAR} />
                </Stack>
              </SectionedCard.Body>
            </SectionedCard>

            <Alert color="blue" variant="light">
              <Text size="sm">
                {NASCE_SEM_AGENTES_ANTES}
                <b>{NASCE_SEM_AGENTES_DESTAQUE}</b>
                {NASCE_SEM_AGENTES_DEPOIS}
              </Text>
            </Alert>

            <Group justify="flex-end" gap="sm">
              <Button variant="default" component={Link} to="/applications">
                Cancelar
              </Button>
              <Button type="submit" disabled={senha.enviando || senha.bloqueado}>
                {senha.enviando ? 'Criando…' : 'Criar aplicação'}
              </Button>
            </Group>
          </Stack>
        </Box>
      )}
    </Stack>
  );
}
