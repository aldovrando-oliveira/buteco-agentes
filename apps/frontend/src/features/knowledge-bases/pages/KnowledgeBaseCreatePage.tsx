import { useMemo, useState } from 'react';
import { Stack, Title } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { useNavigate } from 'react-router';
import type { UseQueryResult } from '@tanstack/react-query';
import { BackLink } from '../../../components/layout/BackLink';
import { useCreateKnowledgeBaseMutation, useKnowledgeBasesQuery } from '../api/useKnowledgeBases';
import { useConnectorFoldersQuery, useConnectorProvidersQuery } from '../api/useConnectors';
import { connectorsBaseUrl } from '../api/connectorsApi';
import { KnowledgeBaseForm } from '../components/KnowledgeBaseForm';
import {
  KnowledgeBaseOriginCard,
  type ProvidersState,
} from '../components/KnowledgeBaseOriginCard';
import {
  FolderPickerModal,
  type FolderLevelState,
  type FolderPathStep,
} from '../components/FolderPickerModal';
import { ApiError } from '../api/knowledgeBasesApi';
import { connectorErrorMessage } from '../utils/connectorErrors';
import { buildFoldersInUse } from '../utils/foldersInUse';
import type { ConnectorFolder } from '../types/connectors';
import type {
  CreateKnowledgeBaseInput,
  CreateManualKnowledgeBaseInput,
  KnowledgeBaseContentMode,
} from '../types/knowledgeBase';

function fieldErrorsFrom(error: unknown): Record<string, string> | undefined {
  if (error instanceof ApiError && error.status === 400 && error.problem?.errors) {
    return Object.fromEntries(
      Object.entries(error.problem.errors).map(([field, messages]) => [field, messages[0]]),
    );
  }
  return undefined;
}

const FOLDER_REQUIRED = 'Escolha a pasta que a base vai acompanhar.';

// Estado da consulta como a tela o mostra. Nova tentativa depois de erro volta a
// "carregando", e não continua exibindo o erro antigo enquanto a resposta não
// chega.
function queryView<T>(query: UseQueryResult<T>): 'loading' | 'error' | 'success' {
  if (query.isError && query.isFetching) return 'loading';
  if (query.isError) return 'error';
  if (query.data !== undefined) return 'success';
  return 'loading';
}

// Nota de rodapé da base sincronizada (design.md, D6, C5): SEM prazo para a
// primeira sincronização. A rodada é do apps/connectors, periódica
// (openspec/specs/knowledge-sync-cycle, "logo depois do boot e a cada 5 minutos"),
// e só roda com `Api__BaseUrl` configurada nele — em produção, depois da #119.
// GATILHO: se o cadastro passar a disparar a primeira sincronização, esta nota
// pode ganhar o "logo depois de criar" que hoje seria falso.
const SYNCED_FOOTER_NOTE =
  'Os documentos não são carregados nesta tela: entram pela sincronização com a pasta, depois de criar a base. A partir daí, incluir, alterar ou excluir arquivos na pasta é o que muda a base.';

export function KnowledgeBaseCreatePage() {
  const navigate = useNavigate();
  const mutation = useCreateKnowledgeBaseMutation();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | undefined>();

  // Endereço do apps/connectors resolvido no build (design.md, D1): nulo desliga
  // a origem Sincronizada, e nenhuma consulta abaixo é habilitada.
  const syncAvailable = connectorsBaseUrl() !== null;
  const [mode, setMode] = useState<KnowledgeBaseContentMode>('Manual');
  const synced = mode === 'Synced' && syncAvailable;

  const [chosenProviderKey, setChosenProviderKey] = useState<string | null>(null);
  const [folder, setFolder] = useState<ConnectorFolder | null>(null);
  const [folderError, setFolderError] = useState<string | undefined>();
  const [submitError, setSubmitError] = useState<string | undefined>();

  const [pickerOpen, setPickerOpen] = useState(false);
  const [path, setPath] = useState<FolderPathStep[]>([]);
  const [pickerSelection, setPickerSelection] = useState<ConnectorFolder | null>(null);

  // Provedores só depois de marcar Sincronizada: o cadastro manual não chama o
  // apps/connectors.
  const providersQuery = useConnectorProvidersQuery({ enabled: synced });
  const providerList = providersQuery.data;
  // Com um provedor só, ele vem escolhido.
  const providerKey =
    chosenProviderKey ?? (providerList?.length === 1 ? providerList[0].key : null);
  const provider = providerList?.find((item) => item.key === providerKey);

  // Pastas e bases só com o seletor aberto. A listagem de bases é o cruzamento de
  // pasta em uso (design.md, D4), e só serve ao seletor.
  const foldersQuery = useConnectorFoldersQuery(providerKey, path.at(-1)?.id ?? null, {
    enabled: synced && pickerOpen,
  });
  const basesQuery = useKnowledgeBasesQuery({ enabled: synced && pickerOpen });
  const foldersInUse = useMemo(() => buildFoldersInUse(basesQuery.data ?? []), [basesQuery.data]);

  const providersState: ProvidersState = (() => {
    const view = queryView(providersQuery);
    if (view === 'error') {
      return {
        status: 'error',
        message: connectorErrorMessage(providersQuery.error, 'navigation'),
        onRetry: () => void providersQuery.refetch(),
      };
    }
    if (view === 'success') return { status: 'success', providers: providerList ?? [] };
    return { status: 'loading' };
  })();

  const levelState: FolderLevelState = (() => {
    const view = queryView(foldersQuery);
    if (view === 'error') {
      return {
        status: 'error',
        message: connectorErrorMessage(foldersQuery.error, 'navigation', providerKey),
      };
    }
    if (view === 'success') return { status: 'success', folders: foldersQuery.data ?? [] };
    return { status: 'loading' };
  })();

  const handleModeChange = (next: KnowledgeBaseContentMode) => {
    setMode(next);
    setFolderError(undefined);
    setSubmitError(undefined);
  };

  const handleProviderChange = (key: string) => {
    if (key === providerKey) return;
    setChosenProviderKey(key);
    // A pasta é do provedor: trocar de provedor a limpa.
    setFolder(null);
  };

  const openPicker = () => {
    setPath([]);
    setPickerSelection(folder);
    setPickerOpen(true);
  };

  const confirmPicker = () => {
    setFolder(pickerSelection);
    setFolderError(undefined);
    setSubmitError(undefined);
    setPickerOpen(false);
  };

  const validateOrigin = () => {
    if (!synced) return true;
    if (!providerKey || !folder) {
      setFolderError(FOLDER_REQUIRED);
      return false;
    }
    return true;
  };

  const handleSubmit = (values: CreateManualKnowledgeBaseInput) => {
    setFieldErrors(undefined);
    setSubmitError(undefined);

    // O corpo manual é o de antes, só nome e descrição (design.md, D8).
    const input: CreateKnowledgeBaseInput =
      synced && providerKey && folder
        ? {
            name: values.name,
            description: values.description,
            contentMode: 'Synced',
            provider: providerKey,
            folderId: folder.id,
          }
        : { name: values.name, description: values.description };

    mutation.mutate(input, {
      onSuccess: (knowledgeBase) => {
        notifications.show({
          color: 'green',
          title: 'Base cadastrada',
          message: `"${knowledgeBase.name}" foi cadastrada com sucesso.`,
        });
        navigate(`/knowledge-bases/${knowledgeBase.id}`);
      },
      onError: (error) => {
        const validation = fieldErrorsFrom(error);
        if (validation) {
          // `provider` e `folderId` são do card de Origem: aparecem no campo da
          // pasta.
          const { provider: providerFieldError, folderId: folderFieldError, ...rest } = validation;
          setFieldErrors(rest);
          if (providerFieldError || folderFieldError) {
            setFolderError(folderFieldError ?? providerFieldError);
          }
          return;
        }
        // Erro da base sincronizada fica no card, persistente e com o formulário
        // preenchido; o do cadastro manual continua sendo a notificação de antes
        // (design.md, D3).
        if ('contentMode' in input) {
          setSubmitError(connectorErrorMessage(error, 'create', input.provider));
          return;
        }
        notifications.show({
          color: 'red',
          title: 'Erro ao cadastrar base',
          message: 'Não foi possível cadastrar a base de conhecimento. Tente novamente.',
        });
      },
    });
  };

  return (
    <Stack>
      <BackLink to="/knowledge-bases" label="Bases de conhecimento" />
      <Title order={2}>Nova base de conhecimento</Title>
      <KnowledgeBaseForm
        onSubmit={handleSubmit}
        onCancel={() => navigate('/knowledge-bases')}
        errors={fieldErrors}
        submitting={mutation.isPending}
        validateExtra={validateOrigin}
        footerNote={synced ? SYNCED_FOOTER_NOTE : undefined}
        origin={
          <KnowledgeBaseOriginCard
            mode={mode}
            onModeChange={handleModeChange}
            syncAvailable={syncAvailable}
            providers={providersState}
            providerKey={providerKey}
            onProviderChange={handleProviderChange}
            folder={folder}
            onChooseFolder={openPicker}
            folderError={folderError}
            submitError={submitError}
          />
        }
      />
      {synced && provider && (
        <FolderPickerModal
          opened={pickerOpen}
          onClose={() => setPickerOpen(false)}
          providerKey={provider.key}
          accountEmail={provider.accountEmail}
          path={path}
          onOpenFolder={(item) =>
            setPath((current) => [...current, { id: item.id, name: item.name }])
          }
          onGoTo={(depth) => setPath((current) => current.slice(0, depth))}
          level={levelState}
          onReload={() => void foldersQuery.refetch()}
          foldersInUse={foldersInUse}
          basesUnavailable={basesQuery.isError}
          selected={pickerSelection}
          onSelect={setPickerSelection}
          onConfirm={confirmPicker}
        />
      )}
    </Stack>
  );
}
