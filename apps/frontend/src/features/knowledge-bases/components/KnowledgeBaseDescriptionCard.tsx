import { Stack, Text } from '@mantine/core';
import { SectionedCard } from '../../../components/data/SectionedCard';

interface KnowledgeBaseDescriptionCardProps {
  description: string;
}

// A descrição em seção própria e rotulada, e NÃO como subtítulo do cabeçalho de
// detalhe. A posição é deliberada: como subtítulo ela lê como texto decorativo
// de UI, e ela é campo de runtime — é o texto que o operador escreve DENTRO da
// descrição da tool exposta ao modelo, e é por ele que o modelo decide se a
// pergunta pertence a esta base (design.md, contexto e D7).
//
// "DENTRO DA", e não "É A", e a correção vale o comentário: desde a etapa 4
// (apps/workers/.../Knowledge/Execution/KnowledgeToolDescription.cs:52) a
// descrição da tool é uma cadeia montada — prefixo que nomeia a base, este texto,
// e 441 caracteres fixos de como ler o resultado, iguais para toda base. Era
// verdade quando a 5a-1 escreveu; a etapa 4 a tornou falsa. O formulário carrega
// a mesma correção no preview, e os dois guardas são separados de propósito
// (design.md, D6) — corrigir um e o outro passar verde por vizinhança é o modo
// de falha.
//
// Não existe caminho para descrição ausente: a API a exige não vazia na criação
// e na edição, e o campo do response não é anulável. O callout âmbar que o
// protótipo tem para esse caso não foi implementado, porque seria código para um
// estado inalcançável (D7).
export function KnowledgeBaseDescriptionCard({ description }: KnowledgeBaseDescriptionCardProps) {
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
            Este texto não é mostrado ao cliente. Ele entra na descrição da ferramenta que o agente
            vê, envolvido por instruções fixas do sistema: é por ele que o modelo decide se a
            pergunta pertence a esta base.
          </Text>
        </Stack>
      </SectionedCard.Body>
    </SectionedCard>
  );
}
