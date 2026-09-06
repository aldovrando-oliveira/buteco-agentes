import { Box, Group, Paper, type PaperProps } from '@mantine/core';
import type { ReactNode } from 'react';
import { SectionLabel } from './SectionLabel';

interface SectionedCardProps extends Omit<PaperProps, 'title' | 'children'> {
  /** Rótulo da faixa de cabeçalho. Sem ele, o card não tem faixa. */
  title?: string;
  /** Conteúdo alinhado à direita na faixa, tipicamente uma ação. */
  action?: ReactNode;
  children: ReactNode;
}

interface SectionedCardRowProps {
  children: ReactNode;
}

// Card com cabeçalho em faixa e linhas divididas — o padrão que se repetia no
// catálogo de tools, nas listagens e nos cards de detalhe.
//
// Montado sobre Paper, não sobre Card: no esquema escuro o Card do Mantine usa
// um tom mais claro que a superfície declarada pela identidade visual, o que
// criaria duas cores de superfície no painel.
//
// As linhas vêm de um de dois mecanismos. Conteúdo em div usa Row, que desenha
// a borda inferior. Tabelas entram como filho direto e se dividem sozinhas: o
// Mantine liga withRowBorders por padrão e usa a mesma cor de borda da
// identidade visual (design.md da change frontend-acabamento-telas, D1).
export function SectionedCard({ title, action, children, ...rest }: SectionedCardProps) {
  return (
    <Paper withBorder radius="md" style={{ overflow: 'hidden' }} {...rest}>
      {title && (
        // Altura fixa, e não padding: a faixa que carrega um botão ficava mais
        // alta que a que carrega só o rótulo, e cards lado a lado desalinhavam.
        // Com altura própria, a faixa é a mesma independentemente do conteúdo.
        <Box
          px="md"
          h={40}
          bg="var(--buteco-surface-subtle)"
          style={{ borderBottom: '1px solid var(--mantine-color-default-border)' }}
        >
          <Group justify="space-between" wrap="nowrap" gap="sm" h="100%" align="center">
            <SectionLabel>{title}</SectionLabel>
            {action}
          </Group>
        </Box>
      )}
      {children}
    </Paper>
  );
}

// Uma linha do card. O padding horizontal vive AQUI, e não no card: é o que
// faz o divisor ir de borda a borda em vez de nascer recuado.
//
// A borda entre linhas é regra de irmão adjacente, em index.css, e não estilo
// embutido: só assim ela some da primeira linha sem que o componente precise
// saber a sua posição. A separação da faixa vem da borda inferior dela.
function SectionedCardRow({ children }: SectionedCardRowProps) {
  return (
    <Box px="md" py="sm" data-sectioned-card-row>
      {children}
    </Box>
  );
}

// Corpo contínuo, para o card que não tem linhas divididas — a maioria dos
// cards de detalhe. Existe para que o padding do conteúdo tenha um lugar só,
// já que o card não o aplica (as linhas precisam encostar nas bordas).
function SectionedCardBody({ children }: SectionedCardRowProps) {
  return (
    <Box px="md" py="sm">
      {children}
    </Box>
  );
}

SectionedCard.Row = SectionedCardRow;
SectionedCard.Body = SectionedCardBody;
