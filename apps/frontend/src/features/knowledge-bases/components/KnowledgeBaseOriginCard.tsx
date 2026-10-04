import {
  Alert,
  Anchor,
  Box,
  Button,
  Code,
  CopyButton,
  Divider,
  Group,
  Input,
  Loader,
  Paper,
  Radio,
  Select,
  SimpleGrid,
  Stack,
  Text,
} from '@mantine/core';
import { Folder } from 'lucide-react';
import type { ConnectorFolder, ConnectorProvider } from '../types/connectors';
import type { KnowledgeBaseContentMode } from '../types/knowledgeBase';
import { providerLabel } from '../utils/connectorErrors';

// Estado da consulta de provedores, já resolvido pela página: este componente é
// apresentacional e não importa hook de consulta (convenção 7).
export type ProvidersState =
  | { status: 'loading' }
  | { status: 'error'; message: string; onRetry: () => void }
  | { status: 'success'; providers: ConnectorProvider[] };

export interface KnowledgeBaseOriginCardProps {
  mode: KnowledgeBaseContentMode;
  onModeChange: (mode: KnowledgeBaseContentMode) => void;
  // `false` quando o build não tem VITE_CONNECTORS_BASE_URL (design.md, D1).
  syncAvailable: boolean;
  providers: ProvidersState;
  providerKey: string | null;
  onProviderChange: (key: string) => void;
  folder: ConnectorFolder | null;
  onChooseFolder: () => void;
  folderError?: string;
  submitError?: string;
}

const PROSE_MAX_WIDTH = 620;

interface OriginOptionProps {
  value: KnowledgeBaseContentMode;
  title: string;
  description: string;
  disabled?: boolean;
  note?: string;
}

// Opção em cartão, como nas pranchas 2a e 2b: borda de 2px na cor de destaque e
// fundo sutil quando marcada. O fundo vem de `--mantine-primary-color-light`, que
// o Mantine declara nos dois esquemas (convenção 16), e não do `#f3f7fd` da
// prancha, que só existe no claro.
function OriginOption({ value, title, description, disabled, note }: OriginOptionProps) {
  return (
    <Radio.Card
      value={value}
      disabled={disabled}
      radius="md"
      p={14}
      className="buteco-origin-option"
      // Conteúdo no topo do cartão: o Radio.Card é um botão, que centraliza na
      // vertical, e o cartão vizinho mais alto deslocava o conteúdo deste
      // (conferência, rodada 1).
      style={{
        cursor: disabled ? 'not-allowed' : 'pointer',
        display: 'flex',
        alignItems: 'flex-start',
      }}
    >
      <Group wrap="nowrap" align="flex-start" gap={10}>
        <Radio.Indicator mt={3} disabled={disabled} />
        <Stack gap={4}>
          <Text fw={600} size="sm" c={disabled ? 'dimmed' : undefined}>
            {title}
          </Text>
          <Text fz={13} c="dimmed" style={{ lineHeight: 1.45 }}>
            {description}
          </Text>
          {note && (
            <Text size="xs" c="dimmed" style={{ lineHeight: 1.45 }}>
              {note}
            </Text>
          )}
        </Stack>
      </Group>
    </Radio.Card>
  );
}

function ProviderSection({
  providers,
  providerKey,
  onProviderChange,
}: Pick<KnowledgeBaseOriginCardProps, 'providers' | 'providerKey' | 'onProviderChange'>) {
  // Carregando: nenhum texto de erro enquanto a consulta não respondeu — o
  // defeito que a #99 corrigiu no card de agentes (design.md, D5).
  if (providers.status === 'loading') {
    return (
      <Group gap="xs">
        <Loader size="xs" />
        <Text size="sm" c="dimmed">
          Carregando provedores…
        </Text>
      </Group>
    );
  }

  if (providers.status === 'error') {
    return (
      <Alert color="red" variant="light">
        <Stack gap="xs" align="flex-start">
          <Text size="sm">{providers.message}</Text>
          <Button size="xs" variant="default" onClick={providers.onRetry}>
            Tentar de novo
          </Button>
        </Stack>
      </Alert>
    );
  }

  // `[]` é resposta que chegou dizendo que não há: explicação, nunca erro nem
  // travessão (convenção 13, quarto estado).
  if (providers.providers.length === 0) {
    return (
      <Text size="sm" c="dimmed" maw={PROSE_MAX_WIDTH} style={{ lineHeight: 1.55 }}>
        Nenhum provedor está configurado no serviço de conectores desta instalação. É configuração
        da instalação; a base manual continua disponível.
      </Text>
    );
  }

  const selected = providers.providers.find((provider) => provider.key === providerKey);

  return (
    <Stack gap="lg">
      <Select
        label="Provedor"
        description="Só aparecem provedores configurados nesta instalação."
        withAsterisk
        allowDeselect={false}
        maw={360}
        data={providers.providers.map((provider) => ({
          value: provider.key,
          label: providerLabel(provider.key),
        }))}
        value={providerKey}
        onChange={(value) => value && onProviderChange(value)}
      />

      {selected && (
        // Tom da página, e não o da prévia da descrição: a prancha 2b usa o fundo
        // da página aqui, mais claro que o bloco "Seu texto". A variável é
        // declarada nos dois esquemas (theme.ts, convenção 16).
        <Box
          p="sm"
          bg="var(--buteco-page-bg)"
          style={{
            borderRadius: 'var(--mantine-radius-sm)',
            border: '1px solid var(--mantine-color-default-border)',
          }}
          data-testid="service-account"
        >
          <Stack gap={8}>
            <Text size="sm">
              Compartilhe a pasta com esta conta, como <strong>Leitor</strong>, antes de escolhê-la:
            </Text>
            <Group gap="sm" wrap="wrap">
              <Code fz="sm" style={{ wordBreak: 'break-all' }}>
                {selected.accountEmail}
              </Code>
              <CopyButton value={selected.accountEmail}>
                {({ copied, copy }) => (
                  <Button size="xs" variant="default" onClick={copy}>
                    {copied ? 'Copiado' : 'Copiar'}
                  </Button>
                )}
              </CopyButton>
            </Group>
          </Stack>
        </Box>
      )}
    </Stack>
  );
}

function FolderField({
  folder,
  onChooseFolder,
  folderError,
}: Pick<KnowledgeBaseOriginCardProps, 'folder' | 'onChooseFolder' | 'folderError'>) {
  return (
    <Input.Wrapper
      label="Pasta"
      withAsterisk
      // Afirmação estrutural sobre outro app (convenção 13), lida em
      // apps/connectors/src/Buteco.Connectors/Connectors/GoogleDrive/GoogleDriveFolderContentSource.cs:9-14
      // (Google Doc por exportação; text/x-markdown e text/markdown por download)
      // e na spec google-drive-connector ("Tipo suportado decidido pelo
      // mimeType": subpasta vira `subfolder-not-synced`). GATILHO: um tipo novo
      // suportado lá, ou a sincronização descer em subpastas, torna esta frase
      // falsa.
      description="Entram só os arquivos da raiz da pasta: Google Docs e arquivos .md. Subpastas e outros tipos ficam de fora."
      error={folderError}
    >
      <Group
        justify="space-between"
        wrap="nowrap"
        mt={6}
        px={14}
        py={12}
        style={{
          border: `1px solid ${
            folderError ? 'var(--mantine-color-error)' : 'var(--mantine-color-default-border)'
          }`,
          borderRadius: 'var(--mantine-radius-sm)',
          background: 'var(--mantine-color-body)',
        }}
        data-testid="folder-field"
      >
        <Group gap={10} wrap="nowrap" style={{ minWidth: 0 }}>
          <Folder size={20} strokeWidth={1.7} color="var(--mantine-color-dimmed)" aria-hidden />
          {folder ? (
            <Stack gap={0} style={{ minWidth: 0 }}>
              <Text size="sm" fw={600} truncate>
                {folder.name}
              </Text>
              <Anchor href={folder.webUrl} target="_blank" rel="noopener noreferrer" size="xs">
                Abrir no Drive ↗
              </Anchor>
            </Stack>
          ) : (
            <Text size="sm" c="dimmed">
              Nenhuma pasta escolhida
            </Text>
          )}
        </Group>
        <Button variant="default" onClick={onChooseFolder} style={{ flexShrink: 0 }}>
          {folder ? 'Trocar pasta' : 'Escolher pasta'}
        </Button>
      </Group>
    </Input.Wrapper>
  );
}

// Card "Origem dos documentos" do formulário de nova base (pranchas 2a e 2b). A
// origem não muda depois do cadastro (knowledge-base-catalog: `contentMode`,
// provedor e pasta imutáveis), e por isso o card só existe na criação.
export function KnowledgeBaseOriginCard({
  mode,
  onModeChange,
  syncAvailable,
  providers,
  providerKey,
  onProviderChange,
  folder,
  onChooseFolder,
  folderError,
  submitError,
}: KnowledgeBaseOriginCardProps) {
  const synced = mode === 'Synced' && syncAvailable;
  const providerReady =
    providers.status === 'success' &&
    providers.providers.some((provider) => provider.key === providerKey);

  return (
    <Paper withBorder radius="md" p="lg" data-testid="origin-card">
      <Box
        component="fieldset"
        aria-label="Origem dos documentos"
        style={{ border: 0, margin: 0, padding: 0, minWidth: 0 }}
      >
        <Stack gap={14}>
          <Stack gap={4}>
            <Text fw={600} size="sm">
              Origem dos documentos{' '}
              <Text component="span" c="red" inherit aria-hidden>
                *
              </Text>
            </Text>
            <Text size="xs" c="dimmed">
              Não pode ser alterada depois de criar a base.
            </Text>
          </Stack>

          <Radio.Group
            value={mode}
            onChange={(value) => onModeChange(value as KnowledgeBaseContentMode)}
            aria-label="Origem dos documentos"
          >
            <SimpleGrid cols={{ base: 1, sm: 2 }} spacing={12}>
              <OriginOption
                value="Manual"
                title="Manual"
                description="Você carrega e atualiza os documentos markdown na tela da base."
              />
              <OriginOption
                value="Synced"
                title="Sincronizada"
                description="Os documentos vêm de uma pasta e acompanham o que é incluído, alterado ou excluído nela."
                disabled={!syncAvailable}
                // Sem VITE_CONNECTORS_BASE_URL no build (design.md, D1). A segunda
                // frase é para quem administra a instalação.
                note={
                  syncAvailable
                    ? undefined
                    : 'A sincronização com pastas não está habilitada neste painel. Falta o endereço do serviço de conectores na construção do painel.'
                }
              />
            </SimpleGrid>
          </Radio.Group>

          {synced && (
            <>
              <Divider />
              <Stack gap="lg">
                <ProviderSection
                  providers={providers}
                  providerKey={providerKey}
                  onProviderChange={onProviderChange}
                />
                {providerReady && (
                  <FolderField
                    folder={folder}
                    onChooseFolder={onChooseFolder}
                    folderError={folderError}
                  />
                )}
              </Stack>
            </>
          )}

          {submitError && (
            <Alert color="red" variant="light">
              <Text size="sm">{submitError}</Text>
            </Alert>
          )}
        </Stack>
      </Box>
    </Paper>
  );
}
