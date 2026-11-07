import { Badge, Box, Text } from '@mantine/core';
import { SectionedCard } from '../../../components/data/SectionedCard';
import type { IssuerOverview } from '../api/identityApi';
import { formatDuration } from '../identityTexts';

// "Emissor do Buteco" (requisito "Cartão Emissor do Buteco"). O selo "Operando" é fixo na
// resposta `200`: com o anel fora, o gate responde `503` antes da rota (#211). A validade diz
// "usuários e módulos" na M1, sem aplicações (decisão do dono Q10).

export function IssuerCard({ emissor }: { emissor: IssuerOverview }) {
  const linhas: [string, string, boolean][] = [
    ['Issuer', emissor.issuer, true],
    ['Discovery', emissor.discovery, true],
    ['Endpoint de token', emissor.token, true],
    ['Chaves públicas (JWKS)', emissor.jwks, true],
    ['Validade do token', `${formatDuration(emissor.validadeDoTokenSegundos)} para usuários e módulos`, false],
    [
      'Sessão do painel',
      `Renovada enquanto houver uso; termina depois de ${formatDuration(emissor.sessaoSemAtividadeSegundos)} sem atividade`,
      false,
    ],
  ];
  return (
    <SectionedCard
      title="Emissor do Buteco"
      action={
        <Badge color="green" variant="light" tt="none">
          Operando
        </Badge>
      }
    >
      <Box
        component="dl"
        m={0}
        px="md"
        py="md"
        style={{ display: 'grid', gridTemplateColumns: 'minmax(120px, 170px) minmax(0, 1fr)', rowGap: 8, columnGap: 16 }}
      >
        {linhas.map(([rotulo, valor, mono]) => (
          <Box key={rotulo} style={{ display: 'contents' }}>
            <Text component="dt" size="sm" c="dimmed">
              {rotulo}
            </Text>
            <Text component="dd" size="sm" m={0} ff={mono ? 'monospace' : undefined} style={{ overflowWrap: 'anywhere' }}>
              {valor}
            </Text>
          </Box>
        ))}
      </Box>
    </SectionedCard>
  );
}
