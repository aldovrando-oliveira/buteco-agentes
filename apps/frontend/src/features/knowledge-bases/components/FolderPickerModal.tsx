import {
  ActionIcon,
  Alert,
  Anchor,
  Box,
  Breadcrumbs,
  Button,
  Group,
  Loader,
  Modal,
  Radio,
  ScrollArea,
  Stack,
  Text,
} from '@mantine/core';
import { ChevronRight, Folder, HardDrive } from 'lucide-react';
import type { ConnectorFolder } from '../types/connectors';
import { folderInUseBy, type FoldersInUse } from '../utils/foldersInUse';

// Seletor de pasta em modal (pranchas 2c e 2d). APRESENTACIONAL: o caminho, a
// consulta do nível atual e a escolha moram na página, e este componente não
// importa hook de consulta (convenção 7; design.md, D5).

export interface FolderPathStep {
  id: string;
  name: string;
}

export type FolderLevelState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'success'; folders: ConnectorFolder[] };

export interface FolderPickerModalProps {
  opened: boolean;
  onClose: () => void;
  providerKey: string;
  accountEmail: string;
  // Passos a partir de "Início"; vazio é o nível de cima.
  path: FolderPathStep[];
  onOpenFolder: (folder: ConnectorFolder) => void;
  // Volta ao nível com `depth` passos (0 é "Início").
  onGoTo: (depth: number) => void;
  level: FolderLevelState;
  onReload: () => void;
  foldersInUse: FoldersInUse;
  // A listagem de bases falhou: o cruzamento de pasta em uso não aconteceu.
  basesUnavailable: boolean;
  selected: ConnectorFolder | null;
  onSelect: (folder: ConnectorFolder) => void;
  onConfirm: () => void;
}

// Altura da área rolável escolhida para o modal inteiro ter os 640px das pranchas
// 2c e 2d (conferência, rodada 1).
const LIST_HEIGHT = 417;

interface FolderRowProps {
  folder: ConnectorFolder;
  inUseBy?: string;
  selected: boolean;
  onSelect: (folder: ConnectorFolder) => void;
  onOpen: (folder: ConnectorFolder) => void;
}

function FolderRow({ folder, inUseBy, selected, onSelect, onOpen }: FolderRowProps) {
  // Drive Compartilhado abre e NÃO é escolhido: o acesso da service account à
  // raiz de um Drive Compartilhado não foi medido (#114), e a prancha 2c também
  // não o oferece (design.md, D5).
  const selectable = folder.kind === 'Folder';
  const Icon = folder.kind === 'SharedDrive' ? HardDrive : Folder;

  return (
    <Box
      component="li"
      data-testid={`folder-row-${folder.name}`}
      px="md"
      py={12}
      style={{
        listStyle: 'none',
        borderTop: '1px solid var(--mantine-color-default-border)',
        background: selected ? 'var(--mantine-primary-color-light)' : undefined,
      }}
    >
      <Group justify="space-between" wrap="nowrap" gap="sm">
        <Group gap={10} wrap="nowrap" style={{ minWidth: 0 }}>
          {selectable && (
            <Radio
              aria-label={`Selecionar ${folder.name}`}
              checked={selected}
              disabled={inUseBy !== undefined}
              onChange={() => onSelect(folder)}
            />
          )}
          <Icon
            size={18}
            strokeWidth={1.8}
            aria-hidden
            color={selected ? 'var(--mantine-primary-color-filled)' : 'var(--mantine-color-dimmed)'}
          />
          <Stack gap={0} style={{ minWidth: 0 }}>
            <Text
              size="sm"
              fw={selected ? 600 : undefined}
              c={inUseBy !== undefined ? 'dimmed' : undefined}
              truncate
            >
              {folder.name}
            </Text>
            {/* Sem link para a base: dentro do modal, navegar para o detalhe
                descarta o formulário preenchido (design.md, D4 e D6, C4). */}
            {inUseBy !== undefined && (
              <Text size="xs" c="dimmed">
                Já sincronizada pela base “{inUseBy}”
              </Text>
            )}
          </Stack>
        </Group>
        <ActionIcon
          variant="subtle"
          color="gray"
          size="sm"
          aria-label={`Abrir ${folder.name}`}
          onClick={() => onOpen(folder)}
        >
          <ChevronRight size={16} aria-hidden />
        </ActionIcon>
      </Group>
    </Box>
  );
}

function FolderList({
  label,
  folders,
  props,
}: {
  label?: string;
  folders: ConnectorFolder[];
  props: FolderPickerModalProps;
}) {
  return (
    <Box>
      {label && (
        <Text
          size="xs"
          fw={600}
          tt="uppercase"
          c="dimmed"
          px="md"
          pt="sm"
          pb={6}
          style={{ letterSpacing: '0.06em' }}
        >
          {label}
        </Text>
      )}
      <Box component="ul" aria-label={label} m={0} p={0}>
        {folders.map((folder) => (
          <FolderRow
            key={folder.id}
            folder={folder}
            inUseBy={folderInUseBy(props.foldersInUse, props.providerKey, folder.id)}
            selected={props.selected?.id === folder.id}
            onSelect={props.onSelect}
            onOpen={props.onOpenFolder}
          />
        ))}
      </Box>
    </Box>
  );
}

function LevelContent(props: FolderPickerModalProps) {
  const { level, path, accountEmail, onReload } = props;

  // Carregando: nem erro, nem vazio — a consulta ainda não respondeu (o defeito
  // que a #99 corrigiu no card de agentes).
  if (level.status === 'loading') {
    return (
      <Group gap="xs" p="md">
        <Loader size="xs" />
        <Text size="sm" c="dimmed">
          Carregando pastas…
        </Text>
      </Group>
    );
  }

  // Erro nunca vira lista vazia: pasta sem acesso é 422 na rota, não `200 []`
  // (google-drive-connector, "Pasta sem acesso responde erro, nunca lista vazia").
  if (level.status === 'error') {
    return (
      <Box p="md">
        <Alert color="red" variant="light">
          <Stack gap="xs" align="flex-start">
            <Text size="sm">{level.message}</Text>
            <Button size="xs" variant="default" onClick={onReload}>
              Tentar de novo
            </Button>
          </Stack>
        </Alert>
      </Box>
    );
  }

  const { folders } = level;

  if (path.length === 0) {
    // Nível de cima: `[]` é resposta que chegou dizendo que não há (convenção 13,
    // quarto estado).
    if (folders.length === 0) {
      return (
        <Text size="sm" c="dimmed" p="md">
          Nada foi compartilhado com {accountEmail} ainda.
        </Text>
      );
    }
    const drives = folders.filter((folder) => folder.kind === 'SharedDrive');
    const shared = folders.filter((folder) => folder.kind === 'Folder');
    return (
      <Stack gap="xs">
        {drives.length > 0 && (
          <FolderList label="Drives compartilhados" folders={drives} props={props} />
        )}
        {shared.length > 0 && (
          <FolderList label="Pastas compartilhadas com a conta" folders={shared} props={props} />
        )}
      </Stack>
    );
  }

  if (folders.length === 0) {
    return (
      <Text size="sm" c="dimmed" p="md">
        Esta pasta não tem subpastas.
      </Text>
    );
  }

  // A contagem é a do que a rota devolveu, uma medição: pode ser exibida.
  return (
    <Box>
      <Text size="xs" c="dimmed" px="md" py={10}>
        {folders.length === 1 ? '1 pasta' : `${folders.length} pastas`}
      </Text>
      <FolderList folders={folders} props={props} />
    </Box>
  );
}

export function FolderPickerModal(props: FolderPickerModalProps) {
  const { opened, onClose, path, onGoTo, accountEmail, onReload, basesUnavailable } = props;
  const { selected, onConfirm } = props;

  return (
    <Modal
      opened={opened}
      onClose={onClose}
      title="Escolher pasta"
      size={720}
      padding={0}
      styles={{
        header: {
          padding: '16px 20px',
          borderBottom: '1px solid var(--mantine-color-default-border)',
        },
        title: { fontSize: 18, fontWeight: 600 },
      }}
    >
      <Box
        component="nav"
        aria-label="Caminho"
        px={20}
        py={12}
        style={{ borderBottom: '1px solid var(--mantine-color-default-border)' }}
      >
        <Breadcrumbs separator="›" separatorMargin={6} fz="sm">
          {path.length === 0 ? (
            <Text size="sm" fw={500}>
              Início
            </Text>
          ) : (
            <Anchor component="button" type="button" size="sm" onClick={() => onGoTo(0)}>
              Início
            </Anchor>
          )}
          {path.map((step, index) =>
            index === path.length - 1 ? (
              <Text key={step.id} size="sm" fw={500}>
                {step.name}
              </Text>
            ) : (
              <Anchor
                key={step.id}
                component="button"
                type="button"
                size="sm"
                onClick={() => onGoTo(index + 1)}
              >
                {step.name}
              </Anchor>
            ),
          )}
        </Breadcrumbs>
      </Box>

      <ScrollArea h={LIST_HEIGHT}>
        {/* Sem a listagem de bases, o painel não sabe quais pastas estão em uso, e
            diz isso em vez de afirmar que estão livres (design.md, D4). */}
        {basesUnavailable && (
          <Text size="xs" c="dimmed" px="md" pt="sm">
            Não foi possível conferir quais pastas já são usadas por outras bases. O cadastro
            confere ao criar.
          </Text>
        )}
        <LevelContent {...props} />
      </ScrollArea>

      {/* Fora da rolagem, ao contrário da prancha 2c, que a põe no fim da lista:
          com muitas pastas, "Recarregar" ficaria escondido embaixo delas
          (conferência, rodada 1; design.md, D6, C10). */}
      <Group
        gap="sm"
        px="md"
        py="sm"
        wrap="nowrap"
        align="center"
        style={{ borderTop: '1px solid var(--mantine-color-default-border)' }}
      >
        <Text size="xs" c="dimmed" style={{ lineHeight: 1.55 }}>
          Só aparece o que foi compartilhado com {accountEmail}. Não encontrou a pasta?
          Compartilhe-a com essa conta e recarregue.
        </Text>
        <Button size="xs" variant="default" onClick={onReload} style={{ flexShrink: 0 }}>
          Recarregar
        </Button>
      </Group>

      <Group
        justify="space-between"
        wrap="nowrap"
        gap="sm"
        px={20}
        py={14}
        style={{ borderTop: '1px solid var(--mantine-color-default-border)' }}
      >
        <Text size="xs" c="dimmed">
          {selected ? `Os arquivos da raiz de “${selected.name}” entram na base.` : ''}
        </Text>
        <Group gap="sm" wrap="nowrap">
          <Button variant="default" onClick={onClose}>
            Cancelar
          </Button>
          <Button disabled={!selected} onClick={onConfirm}>
            {selected ? `Selecionar “${selected.name}”` : 'Selecionar pasta'}
          </Button>
        </Group>
      </Group>
    </Modal>
  );
}
