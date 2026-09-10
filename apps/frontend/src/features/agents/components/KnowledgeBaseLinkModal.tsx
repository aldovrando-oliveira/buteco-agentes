import { useState } from 'react';
import { Anchor, Badge, Box, Button, Group, Modal, Stack, Text, TextInput } from '@mantine/core';
import { Link } from 'react-router';
import { SectionedCard } from '../../../components/data/SectionedCard';
import { matchesSearch } from '../../../utils/searchText';
import type { KnowledgeBase } from '../../knowledge-bases/types/knowledgeBase';

interface KnowledgeBaseLinkModalProps {
  opened: boolean;
  // Catálogo completo, buscado pela página e repassado como propriedade
  // (convenção 7): este componente não importa hook de query.
  catalog: KnowledgeBase[];
  // Ids escolhidos no rascunho da aba — não o que está gravado no servidor.
  selectedIds: string[];
  onToggle: (knowledgeBaseId: string) => void;
  onClose: () => void;
}

// Modal de seleção, não de gravação. Alternar uma base mexe no rascunho da aba e
// NÃO emite requisição: o contrato é PUT de conjunto inteiro, e a gravação sai
// uma vez, pela barra de alterações não salvas (design.md, D3 e D12).
//
// O protótipo dizia aqui "O vínculo é salvo na hora" — copy trocada, porque sob
// rascunho ela seria falsa (convenção 13).
export function KnowledgeBaseLinkModal({
  opened,
  catalog,
  selectedIds,
  onToggle,
  onClose,
}: KnowledgeBaseLinkModalProps) {
  const [search, setSearch] = useState('');

  // matchesSearch normaliza acento, como todas as buscas do painel desde
  // frontend-listas-busca-e-colunas. O protótipo compara com indexOf cru, e
  // percorrido ao vivo "cardapio" não acha "Cardápio A La Carte" — regra que o
  // sistema já escreveu vence protótipo (design.md, D6).
  const visible = catalog.filter((knowledgeBase) =>
    matchesSearch(search, knowledgeBase.name, knowledgeBase.description),
  );

  const handleClose = () => {
    setSearch('');
    onClose();
  };

  return (
    <Modal opened={opened} onClose={handleClose} title="Vincular base de conhecimento" size="lg">
      <Stack gap="sm">
        <Text size="sm" c="dimmed">
          As bases escolhidas entram na lista da aba e são gravadas ao salvar. Documentos são
          criados e editados em Conhecimento.
        </Text>

        <TextInput
          aria-label="Buscar por nome ou descrição"
          placeholder="Buscar por nome ou descrição"
          value={search}
          onChange={(event) => setSearch(event.currentTarget.value)}
        />

        {/* A lista rola dentro do modal, com busca e rodapé fixos. Com as 100+
            bases que o handoff declara, sem isto o modal cresce sem limite e o
            "Concluir" sai da tela — achado na conferência manual, com 9 bases
            já passando da dobra. É o mesmo desenho do protótipo (corpo com
            scroll próprio, max-height 80vh). */}
        <Box style={{ maxHeight: '46vh', overflowY: 'auto' }}>
          <SectionedCard>
            {visible.length === 0 ? (
              <SectionedCard.Body>
                {/* Dois vazios distintos: catálogo sem nenhuma base é um estado do
                  sistema, busca sem correspondência é um estado da tela. */}
                <Text size="sm" c="dimmed" ta="center" py="md" data-testid="link-modal-empty">
                  {catalog.length === 0
                    ? 'Nenhuma base cadastrada ainda.'
                    : 'Nenhuma base corresponde à busca.'}
                </Text>
              </SectionedCard.Body>
            ) : (
              visible.map((knowledgeBase) => {
                const selected = selectedIds.includes(knowledgeBase.id);
                return (
                  <SectionedCard.Row key={knowledgeBase.id}>
                    <Group
                      justify="space-between"
                      wrap="nowrap"
                      gap="sm"
                      align="flex-start"
                      data-testid={`link-modal-row-${knowledgeBase.id}`}
                    >
                      <Stack gap={2} style={{ minWidth: 0, flex: 1 }}>
                        <Group gap="xs" wrap="nowrap">
                          <Text size="sm" fw={600}>
                            {knowledgeBase.name}
                          </Text>
                          {/* Acréscimo ao protótipo: ele não marca base inativa
                            aqui, então o operador vincula às cegas uma base que
                            o agente não vai consultar. isActive está no catálogo
                            que a tela já busca (design.md, D5). */}
                          {!knowledgeBase.isActive && (
                            <Badge color="gray" size="sm">
                              Inativa
                            </Badge>
                          )}
                        </Group>
                        <Text size="xs" c="dimmed">
                          {knowledgeBase.description}
                        </Text>
                      </Stack>
                      {/* flex: none pelo mesmo motivo da linha da aba: sem isso
                          o rótulo trunca para "Vinc"/"Vinculad" nas linhas de
                          descrição longa. Achado numa rodada DEPOIS de a mesma
                          correção ter sido feita na aba — são dois componentes,
                          e cada um precisa da sua. */}
                      <Button
                        variant={selected ? 'default' : 'filled'}
                        size="xs"
                        style={{ flex: 'none' }}
                        onClick={() => onToggle(knowledgeBase.id)}
                      >
                        {selected ? 'Vinculada' : 'Vincular'}
                      </Button>
                    </Group>
                  </SectionedCard.Row>
                );
              })
            )}
          </SectionedCard>
        </Box>

        <Group justify="space-between">
          <Anchor component={Link} to="/knowledge-bases/new" size="sm">
            Criar nova base
          </Anchor>
          {/* Só fecha. Não existe salvar aqui: a gravação é a barra da aba. */}
          <Button variant="default" onClick={handleClose}>
            Concluir
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}
