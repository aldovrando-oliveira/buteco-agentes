import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { AgentSkillsCard } from './AgentSkillsCard';
import type { AgentSkill } from '../types/agent';

function renderCard(skills: AgentSkill[]) {
  return render(
    <MantineProvider theme={theme}>
      <AgentSkillsCard skills={skills} />
    </MantineProvider>,
  );
}

describe('AgentSkillsCard', () => {
  it('indica ausência de skills quando a lista é vazia', () => {
    renderCard([]);

    expect(screen.getByText('Nenhuma skill declarada.')).toBeInTheDocument();
    expect(screen.queryByRole('list')).not.toBeInTheDocument();
  });

  it('exibe skill só com nome, sem descrição', () => {
    renderCard([{ name: 'Segunda via de boleto', description: null }]);

    expect(screen.getByText('Segunda via de boleto')).toBeInTheDocument();
    expect(screen.getAllByRole('listitem')).toHaveLength(1);
    expect(screen.queryByText('Nenhuma skill declarada.')).not.toBeInTheDocument();
  });

  it('exibe nome e descrição da skill quando ela tem descrição', () => {
    renderCard([
      { name: 'Cobrança', description: 'Negocia dívidas em atraso' },
      { name: 'Boleto', description: null },
    ]);

    expect(screen.getByText('Cobrança')).toBeInTheDocument();
    expect(screen.getByText('Negocia dívidas em atraso')).toBeInTheDocument();
    expect(screen.getByText('Boleto')).toBeInTheDocument();
    expect(screen.getAllByRole('listitem')).toHaveLength(2);
  });
});
