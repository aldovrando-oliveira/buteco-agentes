import { SimpleGrid, TextInput, Textarea } from '@mantine/core';
import { SectionedCard } from '../../../components/data/SectionedCard';
import { DICA_DESCRICAO, DICA_NOME } from '../applicationsTexts';
import type { CamposAplicacao, ErrosAplicacao } from '../applicationFields';

// A seção "Identificação" da Nova aplicação e de Editar aplicação (quadros `AplicacaoNova` e
// `AplicacaoEditar`), com a conferência que o servidor faria (spec `external-applications`: 1 a
// 80 caracteres aparados, descrição até 280). O painel confere antes para não mandar a senha à
// toa; o `400` do servidor cai nos mesmos campos.

interface ApplicationFormProps {
  campos: CamposAplicacao;
  erros: ErrosAplicacao;
  onChange: (campos: CamposAplicacao) => void;
}

export function ApplicationIdentificationCard({ campos, erros, onChange }: ApplicationFormProps) {
  return (
    <SectionedCard title="Identificação">
      <SectionedCard.Body>
        <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="sm" py={6}>
          <TextInput
            label="Nome"
            value={campos.nome}
            onChange={(event) => onChange({ ...campos, nome: event.currentTarget.value })}
            error={erros.nome}
            description={DICA_NOME}
            inputWrapperOrder={['label', 'input', 'error', 'description']}
          />
        </SimpleGrid>
        <Textarea
          label="Descrição"
          rows={3}
          value={campos.descricao}
          onChange={(event) => onChange({ ...campos, descricao: event.currentTarget.value })}
          error={erros.descricao}
          description={DICA_DESCRICAO}
          inputWrapperOrder={['label', 'input', 'error', 'description']}
          pb={8}
        />
      </SectionedCard.Body>
    </SectionedCard>
  );
}
