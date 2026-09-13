import { Alert, Button, Divider, Group, Loader, Stack, Text } from '@mantine/core';
import { SectionedCard } from '../../../components/data/SectionedCard';
import { isIndexCorrupted, isIndexEmpty } from '../utils/indexDiagnostics';
import { documentsWithFragmentsCount, indexedFragmentTotal } from '../utils/documentIndexing';
import type { KnowledgeDocumentSummary } from '../types/knowledgeDocument';
import type { KnowledgeIndexProvenance } from '../types/knowledgeIndex';

interface KnowledgeIndexDiagnosticsTabProps {
  diagnostics: KnowledgeIndexProvenance[] | undefined;
  isLoading: boolean;
  error: unknown;
  documents: KnowledgeDocumentSummary[] | undefined;
  documentsError: unknown;
  onGoToDocuments: () => void;
}

// Apresentacional: não importa hook de query nem de mutation (convenção 7). A
// página busca e repassa, e a ação de ir para a outra aba chega como callback.

// DUAS ESCALAS, DOIS CARDS, E O DO SISTEMA DITO COMO DO SISTEMA.
//
// O protótipo empilha cinco linhas numa lista só — três do sistema (provedor,
// modelo, dimensão) e duas da base (documentos, fragmentos) — sob um cabeçalho
// que descreve apenas as três primeiras. Com a proveniência global isso deixa de
// ser detalhe visual e vira afirmação errada: o cabeçalho passa a dizer que os
// dados da base são do sistema, ou que os do sistema são da base. As duas
// leituras são falsas (design.md, D7).
const SYSTEM_GROUP_TITLE = 'Como o índice foi construído (sistema)';
const BASE_GROUP_TITLE = 'Volume desta base';

function DiagnosticsRow({
  label,
  value,
  testId,
}: {
  label: string;
  value: string;
  testId?: string;
}) {
  return (
    <Group justify="space-between" gap="sm" wrap="nowrap" align="flex-start" data-testid={testId}>
      <Text size="sm" c="dimmed" style={{ flexShrink: 0 }}>
        {label}
      </Text>
      <Text size="sm" ff="monospace" ta="right" style={{ wordBreak: 'break-all' }}>
        {value}
      </Text>
    </Group>
  );
}

// Uma combinação gravada, exibida igual no caso coerente e no caso corrompido.
// NENHUMA marcação de "atual" ou "correta": apps/api não conhece a configuração
// declarada de embedding, e a rota não diz qual combinação é a pretendida —
// eleger uma seria afirmar mais do que se mediu (convenção 13).
function ProvenanceRows({
  item,
  index,
  total,
}: {
  item: KnowledgeIndexProvenance;
  index: number;
  total: number;
}) {
  return (
    <Stack gap="sm" data-testid={`index-provenance-${index}`}>
      {/* Com mais de uma combinação, cada bloco é NOMEADO pela posição. Medido na
          conferência: sem isso as oito linhas correm juntas e a tela lê como uma
          lista com `Provedor de embedding` repetido, em vez de duas combinações —
          justamente na tela que existe para tornar decidível qual parte
          reindexar. O rótulo é POSICIONAL e nada mais: não diz atual, correta nem
          configurada, porque nada aqui sabe qual é a pretendida. */}
      {total > 1 && (
        <Text size="xs" c="dimmed" fw={600}>
          Combinação {index + 1} de {total}
        </Text>
      )}
      <DiagnosticsRow label="Provedor de embedding" value={item.provider} />
      <DiagnosticsRow label="Modelo de embedding" value={item.model} />
      <DiagnosticsRow label="Dimensão do vetor" value={String(item.dimensions)} />
      <DiagnosticsRow
        label="Fragmentos gravados com esta combinação"
        value={String(item.fragmentCount)}
      />
    </Stack>
  );
}

function SystemGroup({
  diagnostics,
  isLoading,
  error,
}: Pick<KnowledgeIndexDiagnosticsTabProps, 'diagnostics' | 'isLoading' | 'error'>) {
  if (isLoading) {
    return (
      <Group gap="xs">
        <Loader size="sm" />
        <Text size="sm">Lendo a proveniência do índice...</Text>
      </Group>
    );
  }

  // FALHA AO LER NÃO É ÍNDICE VAZIO. Uma requisição que não respondeu não é
  // evidência de ausência — é a mesma régua que a listagem de documentos já usa,
  // e a última frase existe para impedir que alguém colapse os dois caminhos.
  if (error || !diagnostics) {
    return (
      <Alert color="red" data-testid="index-provenance-error">
        Não foi possível ler a proveniência do índice. A consulta não respondeu, então não há como
        dizer com que provedor e modelo o índice foi construído — nem se ele está vazio.
      </Alert>
    );
  }

  // ÍNDICE VAZIO: SÓ A EXPLICAÇÃO.
  //
  // Sem linhas de travessão, e a decisão é de vocabulário: nesta área do painel
  // `—` significa DADO DESCONHECIDO — a consulta não respondeu, ou o registro não
  // veio (ver utils/indexingSummary.ts). Índice vazio é fato conhecido e medido,
  // e usar o mesmo símbolo para os dois apaga a distinção justamente na tela em
  // que o desconhecido também existe, logo acima (design.md, D9).
  //
  // E o texto não nomeia provedor nem modelo nenhum: qual modelo SERIA usado é
  // configuração de outro processo, que apps/api sequer conhece.
  if (isIndexEmpty(diagnostics)) {
    return (
      <Stack gap="xs" align="flex-start" data-testid="index-provenance-empty">
        <Text size="sm">O índice de conhecimento está vazio: nenhum fragmento gravado.</Text>
        <Text size="sm" c="dimmed">
          Provedor, modelo e dimensão passam a aparecer quando o primeiro documento terminar de
          indexar, em qualquer base: eles são lidos do índice, não da configuração pretendida.
        </Text>
      </Stack>
    );
  }

  // A ordem é a da resposta, produzida pela collation do banco na própria
  // consulta. Reordenar aqui introduziria um terceiro comparador no painel — o
  // do .NET contra o do PostgreSQL, que discordam de fato — e faria a tela
  // exibir uma ordem que a API não produziu.
  return (
    <Stack gap="md">
      {isIndexCorrupted(diagnostics) && (
        <Alert
          color="red"
          title="Índice corrompido: mais de uma combinação"
          data-testid="index-corruption"
        >
          <Stack gap="xs">
            <Text size="sm">
              Os fragmentos gravados usam {diagnostics.length} combinações diferentes de provedor,
              modelo e dimensão, quando só pode haver uma. Vetores de modelos diferentes são
              incomparáveis: a busca continua devolvendo resultados, sem erro nenhum, misturando
              escalas.
            </Text>
            <Text size="sm">
              A checagem de integridade da indexação recusa o boot enquanto o índice estiver assim.
              A contagem de fragmentos de cada combinação abaixo diz o tamanho de cada parte — é por
              ela que se decide o que reindexar.
            </Text>
          </Stack>
        </Alert>
      )}

      {diagnostics.map((item, index) => (
        <Stack gap="md" key={`${item.provider}/${item.model}/${item.dimensions}`}>
          {index > 0 && <Divider />}
          <ProvenanceRows item={item} index={index} total={diagnostics.length} />
        </Stack>
      ))}
    </Stack>
  );
}

function BaseGroup({
  documents,
  documentsError,
  onGoToDocuments,
}: Pick<KnowledgeIndexDiagnosticsTabProps, 'documents' | 'documentsError' | 'onGoToDocuments'>) {
  // Listagem indisponível não vira zero: seria afirmar uma contagem que ninguém
  // fez (convenção 13, o terceiro caso — ausência de linha é desconhecido).
  if (documentsError || !documents) {
    return (
      <Alert color="red" data-testid="base-volume-error">
        Não foi possível ler os documentos desta base. Sem a listagem não há como dizer quantos têm
        fragmentos no índice.
      </Alert>
    );
  }

  const comFragmentos = documentsWithFragmentsCount(documents);
  const fragmentos = indexedFragmentTotal(documents);
  const falhas = documents.filter((document) => document.indexingStatus === 'Failed').length;

  return (
    <Stack gap="sm">
      {/* O rótulo diz o PREDICADO que a linha mede, e não reusa a palavra
          `Indexado` do badge da tabela vizinha: com o predicado certo, um
          documento exibido como `Falhou` conta aqui (design.md, D12). */}
      <DiagnosticsRow
        label="Documentos com fragmentos no índice"
        value={`${comFragmentos} de ${documents.length}`}
        testId="base-volume-documents"
      />
      <DiagnosticsRow
        label="Fragmentos desta base no índice"
        value={String(fragmentos)}
        testId="base-volume-fragments"
      />

      {/* A LISTA DE FALHAS NÃO É REPETIDA AQUI — ela aponta.
          O motivo completo e o botão de reindexar já vivem na faixa de falha da
          tabela de documentos (entrega da 5a-2). Repetir criaria duas
          superfícies disparando a mesma mutação, com estados de carregamento
          independentes, e dois lugares onde a cópia do motivo pode divergir
          (design.md, D10). Sem falha, a linha some — nunca "0 falhas" em tom de
          alerta. */}
      {falhas > 0 && (
        <Group justify="space-between" gap="sm" wrap="nowrap" data-testid="base-volume-failures">
          <Text size="sm" c="red">
            {falhas === 1
              ? '1 documento falhou ao indexar'
              : `${falhas} documentos falharam ao indexar`}
          </Text>
          <Button size="compact-sm" variant="default" onClick={onGoToDocuments}>
            Ver na aba Documentos
          </Button>
        </Group>
      )}
    </Stack>
  );
}

export function KnowledgeIndexDiagnosticsTab({
  diagnostics,
  isLoading,
  error,
  documents,
  documentsError,
  onGoToDocuments,
}: KnowledgeIndexDiagnosticsTabProps) {
  return (
    <Stack gap="md" maw={860}>
      {/* Somente leitura, e a razão dita na tela. Conferido contra o código: a
          coerência entre o modelo declarado e o índice gravado é validada no boot
          de apps/workers, sem modo de tolerância e sem bypass. */}
      <Alert color="gray" variant="light" data-testid="diagnostics-readonly-note">
        Somente leitura. Provedor, modelo e dimensão de embedding são configuração de processo,
        validada no boot da indexação — não há formulário aqui. Trocar o modelo exigiria reindexar
        todo o acervo.
      </Alert>

      <SectionedCard title={SYSTEM_GROUP_TITLE}>
        <SectionedCard.Body>
          <SystemGroup diagnostics={diagnostics} isLoading={isLoading} error={error} />
        </SectionedCard.Body>
      </SectionedCard>

      <SectionedCard title={BASE_GROUP_TITLE}>
        <SectionedCard.Body>
          <BaseGroup
            documents={documents}
            documentsError={documentsError}
            onGoToDocuments={onGoToDocuments}
          />
        </SectionedCard.Body>
      </SectionedCard>

      {/* Sem barra de progresso e sem métrica de uso, e a tela DIZ isso para que
          a ausência não seja lida como defeito: o sistema registra o estado de
          cada documento, não o percentual, e não coleta consultas por base. */}
      <Text size="xs" c="dimmed">
        O sistema registra o estado de cada documento, não o percentual de conclusão, e não coleta
        consultas por base — por isso não há barra de progresso nem número de acessos nesta tela.
      </Text>
    </Stack>
  );
}
