import { Group, Radio, Stack, Text } from '@mantine/core';

export interface RadioCardOptionProps {
  value: string;
  title: string;
  description: string;
  disabled?: boolean;
  note?: string;
}

// Opção de um `Radio.Group` em forma de cartão: borda de 2px na cor de destaque
// e fundo sutil quando marcada (`.buteco-radio-card-option`, em index.css). O
// fundo vem de `--mantine-primary-color-light`, que o Mantine declara nos dois
// esquemas (convenção 16), e não do hex de prancha, que só existe no claro.
//
// Extraído do `OriginOption` de `KnowledgeBaseOriginCard` quando o formulário
// de servidor MCP virou o segundo consumidor (design.md da change
// frontend-mcp-autenticacao-header-customizado, D1): copiar o componente seria
// copiar a regra do estado marcado e o alinhamento abaixo.
export function RadioCardOption({
  value,
  title,
  description,
  disabled,
  note,
}: RadioCardOptionProps) {
  return (
    <Radio.Card
      value={value}
      disabled={disabled}
      radius="md"
      p={14}
      className="buteco-radio-card-option"
      // Conteúdo no topo do cartão: o Radio.Card é um botão, que centraliza na
      // vertical, e o cartão vizinho mais alto deslocava o conteúdo deste
      // (conferência de frontend-cadastro-base-sincronizada, rodada 1).
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
