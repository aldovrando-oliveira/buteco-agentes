import { Badge, Table, Text } from '@mantine/core';
import { SectionedCard } from '../../../components/data/SectionedCard';
import { SectionLabel } from '../../../components/data/SectionLabel';
import { formatDate } from '../../../utils/panelDates';
import type { SigningKeys } from '../api/identityApi';
import { formatDuration, keyBadge, nextStepText, type KeyTone } from '../identityTexts';

// "Chaves de assinatura" (requisito "Cartão Chaves de assinatura"). As linhas vêm na ordem da
// resposta, que a tela não reordena (#211), e as durações saem dos campos da rota.

const COR: Record<KeyTone, string> = { green: 'green', blue: 'blue', gray: 'gray', neutral: 'gray' };

export function SigningKeysCard({ chaves }: { chaves: SigningKeys }) {
  return (
    <SectionedCard
      title="Chaves de assinatura"
      action={
        <Text size="xs" c="dimmed">
          Troca automática a cada {formatDuration(chaves.intervaloDeTrocaSegundos)}
        </Text>
      }
    >
      <Text size="sm" c="dimmed" px="md" py="sm" style={{ borderBottom: '1px solid var(--mantine-color-default-border)' }}>
        O Buteco assina cada token com uma chave privada que nunca sai do servidor. A parte pública fica em{' '}
        <Text span ff="monospace" size="xs">
          /.well-known/jwks
        </Text>
        , e é com ela que inbox, connectors e qualquer aplicação conferem se o token é legítimo. A próxima chave aparece{' '}
        {formatDuration(chaves.prePublicacaoSegundos)} antes de começar a assinar, e a anterior continua publicada por{' '}
        {formatDuration(chaves.retencaoSegundos)} depois de parar, para os tokens e as sessões em andamento seguirem
        válidos. A troca acontece em até {formatDuration(chaves.verificacaoSegundos)} depois da data marcada.
      </Text>
      <Table.ScrollContainer minWidth={640}>
        <Table>
          <Table.Thead bg="var(--buteco-surface-subtle)">
            <Table.Tr>
              {['Identificador (kid)', 'Estado', 'Criada em', 'Próximo passo'].map((c) => (
                <Table.Th key={c}>
                  <SectionLabel>{c}</SectionLabel>
                </Table.Th>
              ))}
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {chaves.chaves.map((chave) => {
              const selo = keyBadge(chave.estado);
              return (
                <Table.Tr key={chave.kid}>
                  <Table.Td>
                    <Text size="xs" ff="monospace">
                      {chave.kid}
                    </Text>
                  </Table.Td>
                  <Table.Td>
                    <Badge color={COR[selo.tom]} variant={selo.tom === 'neutral' ? 'outline' : 'light'} tt="none" data-tom={selo.tom}>
                      {selo.texto}
                    </Badge>
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm">{formatDate(chave.criadaEm)}</Text>
                  </Table.Td>
                  <Table.Td>
                    <Text size="sm" c={chave.estado === 'em-uso' ? undefined : 'dimmed'}>
                      {nextStepText(chave)}
                    </Text>
                  </Table.Td>
                </Table.Tr>
              );
            })}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
    </SectionedCard>
  );
}
