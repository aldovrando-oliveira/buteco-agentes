import { Stack, Text } from '@mantine/core';
import { SectionedCard } from '../../../components/data/SectionedCard';

interface KnowledgeBaseDescriptionCardProps {
  description: string;
}

// A descrição em seção própria e rotulada, e NÃO como subtítulo do cabeçalho de
// detalhe. A posição é deliberada: como subtítulo ela lê como texto decorativo
// de UI, e ela é campo de runtime — é o texto que vira a descrição da tool
// exposta ao modelo, e é por ele que o modelo decide se a pergunta pertence a
// esta base (design.md, contexto e D7).
//
// Não existe caminho para descrição ausente: a API a exige não vazia na criação
// e na edição, e o campo do response não é anulável. O callout âmbar que o
// protótipo tem para esse caso não foi implementado, porque seria código para um
// estado inalcançável (D7).
export function KnowledgeBaseDescriptionCard({
  description,
}: KnowledgeBaseDescriptionCardProps) {
  return (
    <SectionedCard title="Descrição — texto lido pelo modelo">
      <SectionedCard.Body>
        <Stack gap="xs">
          {/* Borda esquerda na cor de destaque: a citação marca que o texto é
              lido por outro leitor, não escrito para esta tela. */}
          <Text
            size="sm"
            pl="sm"
            style={{
              borderLeft: '3px solid var(--mantine-primary-color-filled)',
              whiteSpace: 'pre-wrap',
            }}
            data-testid="knowledge-base-description"
          >
            {description}
          </Text>
          <Text size="xs" c="dimmed">
            Este texto não é mostrado ao cliente. Ele é a descrição da ferramenta que o agente vê: é
            por ele que o modelo decide se a pergunta pertence a esta base.
          </Text>
        </Stack>
      </SectionedCard.Body>
    </SectionedCard>
  );
}
