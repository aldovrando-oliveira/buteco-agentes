import { Text } from '@mantine/core';
import { SectionedCard } from '../../../components/data/SectionedCard';

// No lugar da área de documentos, uma nota de SEQUENCIAMENTO — não um estado
// vazio. "Nenhum documento nesta base" afirmaria que a base foi consultada e
// está vazia, quando esta etapa não consulta documento nenhum: o mesmo
// raciocínio que a etapa 1 aplicou à contagem de fragmentos (design.md, D4,
// convenção 13).
//
// Silêncio total foi descartado: quem abre a base vem justamente pelos
// documentos, e nada na tela explicaria a ausência.
export function KnowledgeBaseDocumentsPlaceholder() {
  return (
    <SectionedCard title="Documentos">
      <SectionedCard.Body>
        <Text size="sm" c="dimmed" data-testid="documents-placeholder">
          A gestão de documentos desta base chega na próxima etapa. Esta tela ainda não consulta os
          documentos, então não tem como dizer quantos existem.
        </Text>
      </SectionedCard.Body>
    </SectionedCard>
  );
}
