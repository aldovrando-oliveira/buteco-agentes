import { describe, expect, it } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MantineProvider } from '@mantine/core';
import { useForm } from '@mantine/form';
import { theme } from '../../../theme';
import { AgentSkillsFields } from './AgentSkillsFields';
import type { AgentFormValues, AgentSkillFormValue } from './agentFormValues';

const emptyValues: Omit<AgentFormValues, 'skills'> = {
  name: '',
  description: '',
  instructions: '',
  provider: '',
  model: '',
};

// Host mínimo: o componente só funciona dentro de um useForm que tenha
// `skills`; o <pre> expõe form.values.skills para as asserções.
function Harness({
  initialSkills = [],
  nameErrors,
}: {
  initialSkills?: AgentSkillFormValue[];
  nameErrors?: Record<number, string>;
}) {
  const form = useForm<AgentFormValues>({
    initialValues: { ...emptyValues, skills: initialSkills },
  });
  return (
    <>
      <AgentSkillsFields form={form} nameErrors={nameErrors} />
      <pre data-testid="skills-json">{JSON.stringify(form.values.skills)}</pre>
    </>
  );
}

function renderHarness(props?: Parameters<typeof Harness>[0]) {
  return render(
    <MantineProvider theme={theme}>
      <Harness {...props} />
    </MantineProvider>,
  );
}

function readSkills(): AgentSkillFormValue[] {
  return JSON.parse(screen.getByTestId('skills-json').textContent ?? '[]');
}

describe('AgentSkillsFields', () => {
  it('começa sem linhas e exibe o hint de que skills não afetam o runtime', () => {
    renderHarness();

    expect(screen.queryByLabelText(/nome da skill/i)).not.toBeInTheDocument();
    expect(screen.getByText(/não afetam o runtime/i)).toBeInTheDocument();
  });

  it('"Adicionar skill" insere uma linha vazia com nome e descrição', async () => {
    const user = userEvent.setup();
    renderHarness();

    await user.click(screen.getByRole('button', { name: /adicionar skill/i }));

    expect(screen.getByLabelText(/nome da skill/i)).toHaveValue('');
    expect(screen.getByLabelText(/descrição da skill/i)).toHaveValue('');
    expect(readSkills()).toEqual([{ name: '', description: '' }]);
  });

  it('editar nome e descrição de uma linha reflete em form.values.skills', async () => {
    const user = userEvent.setup();
    renderHarness();

    await user.click(screen.getByRole('button', { name: /adicionar skill/i }));
    await user.type(screen.getByLabelText(/nome da skill/i), 'Segunda via de boleto');
    await user.type(screen.getByLabelText(/descrição da skill/i), 'Emite boleto atualizado');

    expect(readSkills()).toEqual([
      { name: 'Segunda via de boleto', description: 'Emite boleto atualizado' },
    ]);
  });

  it('remover uma linha tira a skill certa de form.values.skills', async () => {
    const user = userEvent.setup();
    renderHarness({
      initialSkills: [
        { name: 'Boleto', description: '' },
        { name: 'Cobrança', description: 'Negocia dívidas' },
      ],
    });

    await user.click(screen.getByRole('button', { name: 'Remover skill 1' }));

    expect(readSkills()).toEqual([{ name: 'Cobrança', description: 'Negocia dívidas' }]);
    expect(screen.getByLabelText(/nome da skill/i)).toHaveValue('Cobrança');
  });

  it('exibe o erro vindo do servidor na linha indicada', () => {
    renderHarness({
      initialSkills: [
        { name: 'Boleto', description: '' },
        { name: '', description: '' },
      ],
      nameErrors: { 1: 'O nome da skill é obrigatório.' },
    });

    const secondRow = screen.getByTestId('agent-skill-row-1');
    expect(within(secondRow).getByText('O nome da skill é obrigatório.')).toBeInTheDocument();
    expect(
      within(screen.getByTestId('agent-skill-row-0')).queryByText('O nome da skill é obrigatório.'),
    ).not.toBeInTheDocument();
  });
});
