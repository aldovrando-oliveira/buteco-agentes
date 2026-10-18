import { useState } from 'react';
import {
  Alert,
  Box,
  Button,
  Divider,
  Group,
  Paper,
  PasswordInput,
  Radio,
  SimpleGrid,
  Stack,
  Text,
  TextInput,
  Textarea,
} from '@mantine/core';
import { useForm } from '@mantine/form';
import { RadioCardOption } from '../../../components/forms/RadioCardOption';
import { SectionLabel } from '../../../components/data/SectionLabel';
import { ApiError } from '../api/mcpServersApi';
import {
  useTestSavedMcpServerConnectionMutation,
  useTestUnsavedMcpServerConnectionMutation,
} from '../api/useMcpServers';
import { authTypeLabels } from '../utils/authTypeLabels';
import { connectionTestMode } from '../utils/connectionTestMode';
import { ConnectionTestResultAlert, type ConnectionTestResult } from './ConnectionTestResultAlert';
import type { CreateMcpServerInput, McpServerAuthType } from '../types/mcpServer';

interface McpServerFormValues {
  name: string;
  description: string;
  url: string;
  authType: McpServerAuthType;
  // Fica no estado mesmo quando o tipo deixa de ser CustomHeader, para quem
  // passa pelos cartões e volta não perder o que digitou; só não é enviado
  // (design.md da change frontend-mcp-autenticacao-header-customizado, D4).
  authHeaderName: string;
  credential: string;
}

type FieldName = keyof McpServerFormValues;

interface McpServerFormProps {
  onSubmit: (values: CreateMcpServerInput) => void;
  onCancel: () => void;
  errors?: Record<string, string>;
  submitting?: boolean;
  // Em edição, `authType` é também o tipo SALVO do servidor, que decide os
  // avisos de troca de tipo e o teste de conexão (D4). Não há prop separada
  // para ele: seria a segunda via do mesmo valor, vindo da mesma resposta.
  initialValues?: CreateMcpServerInput;
  submitLabel?: string;
  mode?: 'create' | 'edit';
  // Necessário para testar a conexão do servidor já salvo, no caso em que
  // a credencial fica em branco durante a edição (Decision 6 do design.md
  // da change frontend-mcp-servidor-uso-e-diagnostico).
  mcpServerId?: string;
}

interface TestedConnectionConfig {
  url: string;
  authType: McpServerAuthType;
  authHeaderName: string;
  credential: string;
}

// O que o último teste devolveu: um resultado de conexão, ou um 400 da API,
// que não é resultado de conexão nenhuma (D5).
type TestOutcome =
  | { kind: 'result'; result: ConnectionTestResult }
  | { kind: 'invalid'; unmatchedMessages: string[] };

// Campos da tela que podem receber um 400 do teste. Chave fora daqui vai no
// próprio aviso, para não sumir (D5).
const testErrorFields: FieldName[] = ['url', 'authType', 'authHeaderName', 'credential'];

// Descrições dos cartões. As de Bearer e de header customizado afirmam como
// apps/api e apps/workers montam o header — ver o comentário da caixa de prévia
// abaixo, que vale para estas duas frases também.
const authTypeDescriptions: Record<McpServerAuthType, string> = {
  None: 'O servidor não exige credencial.',
  BearerToken: 'Envia o token em Authorization: Bearer.',
  CustomHeader: 'Envia o token no header que o serviço exige, como X-API-TOKEN.',
};

const authTypeOrder: McpServerAuthType[] = ['None', 'BearerToken', 'CustomHeader'];

// Corte do empilhamento dos cartões, por largura do FORMULÁRIO (container
// query), não da viewport: a navbar de 224px colapsa em `sm` e mudaria o corte
// por baixo. Uma coluna ou três, nunca duas, que deixariam um cartão órfão.
// MEDIDO em 18/10/2026 (D3, tarefa 7.4 do tasks.md): com container de 760px,
// o pior caso — o cartão "Token em header customizado" marcado, borda de 2px —
// deixa 183px de largura útil para um título de 169,02px (IBM Plex Sans 600,
// 12px), em uma linha. GATILHO: título, fonte, tamanho, padding do cartão ou
// tema mudarem; medir de novo antes de mexer no número.
const cardsColumns = { base: 1, '760px': 3 };

function toFormValues(input?: CreateMcpServerInput): McpServerFormValues {
  return {
    name: input?.name ?? '',
    description: input?.description ?? '',
    url: input?.url ?? '',
    authType: input?.authType ?? 'None',
    authHeaderName: input?.authHeaderName ?? '',
    credential: '',
  };
}

// Superfície própria: o fundo da página deixou de ser branco (change
// frontend-tema-identidade-visual, D4/D12), então o conteúdo precisa declarar
// a sua em vez de herdar o branco do body.
export function McpServerForm({
  onSubmit,
  onCancel,
  errors,
  submitting,
  initialValues,
  submitLabel = 'Cadastrar servidor',
  mode = 'create',
  mcpServerId,
}: McpServerFormProps) {
  const isEditing = mode === 'edit';
  const savedAuthType: McpServerAuthType = initialValues?.authType ?? 'None';

  // Precedência entre o erro da página (400 do submit, em `errors`) e o do
  // formulário (`form.errors`), D5: o erro da página de um campo deixa de ser
  // exibido quando o campo é editado, ou quando um 400 do teste é aplicado a
  // ele. Sem isso, a mensagem do submit ficava no campo até o próximo submit,
  // falando de um valor que já não está lá, e escondia o 400 do teste no mesmo
  // campo. Recusada: exibir o mais recente entre os dois, que exige ordenar as
  // duas fontes e, enquanto se edita sem testar, ainda mostra a do submit.
  const [pageErrorsDismissed, setPageErrorsDismissed] = useState<Set<string>>(() => new Set());
  const [pageErrorsSeen, setPageErrorsSeen] = useState(errors);
  if (errors !== pageErrorsSeen) {
    // Um `errors` novo é um submit novo: o que foi editado antes dele não conta.
    setPageErrorsSeen(errors);
    setPageErrorsDismissed(new Set());
  }
  const dismissPageErrors = (fields: string[]) =>
    setPageErrorsDismissed((current) => new Set([...current, ...fields]));

  const form = useForm<McpServerFormValues>({
    initialValues: toFormValues(initialValues),
    onValuesChange: (values, previous) => {
      const changed = (Object.keys(values) as FieldName[]).filter(
        (field) => values[field] !== previous[field],
      );
      if (changed.length > 0) {
        dismissPageErrors(changed);
      }
    },
    // Só obrigatoriedade (D5). O formato do nome do header e do token é da API,
    // que devolve 400 com a mensagem; uma segunda lista de headers reservados
    // aqui envelheceria (regra 4 da #168).
    validate: {
      name: (value) => (value.trim() ? null : 'O nome do servidor MCP é obrigatório.'),
      url: (value) => (value.trim() ? null : 'A URL do servidor MCP é obrigatória.'),
      authHeaderName: (value, values) =>
        values.authType === 'CustomHeader' && !value.trim()
          ? 'Informe o nome do header em que o serviço espera o token.'
          : null,
      credential: (value, values) => {
        if (values.authType === 'None' || value.trim()) {
          return null;
        }
        if (!isEditing) {
          return 'A credencial é obrigatória para o tipo de autenticação informado.';
        }
        // Em edição, em branco mantém a credencial atual — salvo na troca de
        // tipo, em que a API a apaga e exige uma nova (D4 da #160).
        return values.authType !== savedAuthType
          ? `Informe a credencial nova para o tipo ${authTypeLabels[values.authType]}.`
          : null;
      },
    },
  });

  const [testState, setTestState] = useState<
    { config: TestedConnectionConfig; outcome: TestOutcome } | undefined
  >();
  const testMutation = useTestUnsavedMcpServerConnectionMutation();
  const testSavedMutation = useTestSavedMcpServerConnectionMutation();

  const { authType } = form.values;
  const isCustomHeader = authType === 'CustomHeader';
  const typeChanged = isEditing && authType !== savedAuthType;
  // Em edição com o tipo mantido, credencial em branco mantém a atual.
  const keepsSavedCredential = isEditing && !typeChanged;

  const testMode = connectionTestMode({
    isEditing,
    hasServerId: !!mcpServerId,
    savedAuthType,
    authType,
    credential: form.values.credential,
  });

  const currentConfig: TestedConnectionConfig = {
    url: form.values.url,
    authType,
    authHeaderName: form.values.authHeaderName,
    credential: form.values.credential,
  };
  // O nome do header entra na comparação: sem ele, um sucesso continuaria na
  // tela depois de editar o nome, para um header que nunca foi testado (V4).
  const testOutcome =
    testState &&
    testState.config.url === currentConfig.url &&
    testState.config.authType === currentConfig.authType &&
    testState.config.authHeaderName === currentConfig.authHeaderName &&
    testState.config.credential === currentConfig.credential
      ? testState.outcome
      : undefined;

  const fieldError = (field: FieldName) =>
    (pageErrorsDismissed.has(field) ? undefined : errors?.[field]) ?? form.errors[field];

  const showResult = (
    config: TestedConnectionConfig,
    result: Omit<ConnectionTestResult, 'testedAt'>,
  ) =>
    setTestState({
      config,
      outcome: { kind: 'result', result: { ...result, testedAt: new Date() } },
    });

  const showUnexpectedFailure = (config: TestedConnectionConfig) =>
    showResult(config, {
      success: false,
      failureReason: null,
      message: 'Não foi possível testar a conexão. Tente novamente.',
    });

  const handleTestConnection = () => {
    const config = currentConfig;

    if (testMode === 'blocked') {
      return;
    }

    // Só obrigatoriedade, antes da requisição, como a URL sempre foi. Com
    // CustomHeader a API exige nome e token no teste não salvo (D5); barrar
    // aqui deixa inalcançáveis as duas mensagens dela que citam o nome
    // técnico `CustomHeader`.
    const missing: [FieldName, string][] = [];
    if (!config.url.trim()) {
      missing.push(['url', 'Informe a Url antes de testar.']);
    }
    if (testMode === 'unsaved' && isCustomHeader) {
      if (!config.authHeaderName.trim()) {
        missing.push(['authHeaderName', 'Informe o nome do header antes de testar.']);
      }
      if (!config.credential.trim()) {
        missing.push(['credential', 'Informe o token antes de testar.']);
      }
    }
    if (missing.length > 0) {
      missing.forEach(([field, message]) => form.setFieldError(field, message));
      dismissPageErrors(missing.map(([field]) => field));
      return;
    }

    if (testMode === 'saved') {
      testSavedMutation.mutate(mcpServerId!, {
        onSuccess: (result) => showResult(config, result),
        onError: () => showUnexpectedFailure(config),
      });
      return;
    }

    testMutation.mutate(
      {
        url: config.url,
        authType: config.authType,
        credential: config.credential || undefined,
        ...(config.authType === 'CustomHeader' ? { authHeaderName: config.authHeaderName } : {}),
      },
      {
        onSuccess: (result) => showResult(config, result),
        onError: (error) => {
          if (error instanceof ApiError && error.status === 400 && error.problem?.errors) {
            // Um 400 do teste não é resultado de conexão: vai para os campos,
            // como no submit, e não para o alerta "Falha na conexão" (D5, que
            // substitui a Decision 1 de frontend-mcp-servidores-catalogo).
            const unmatchedMessages: string[] = [];
            const appliedFields: string[] = [];
            Object.entries(error.problem.errors).forEach(([field, messages]) => {
              if ((testErrorFields as string[]).includes(field)) {
                form.setFieldError(field, messages[0]);
                appliedFields.push(field);
              } else {
                unmatchedMessages.push(messages[0]);
              }
            });
            dismissPageErrors(appliedFields);
            setTestState({ config, outcome: { kind: 'invalid', unmatchedMessages } });
            return;
          }
          showUnexpectedFailure(config);
        },
      },
    );
  };

  const handleSubmit = (values: McpServerFormValues) => {
    onSubmit({
      name: values.name,
      description: values.description,
      url: values.url,
      authType: values.authType,
      credential:
        values.authType === 'None' || !values.credential.trim() ? undefined : values.credential,
      // Como foi digitado, sem `trim`: a API recusa espaço com mensagem
      // própria, e aparar aqui seria corrigir no cliente o que é dela (D4).
      ...(values.authType === 'CustomHeader' ? { authHeaderName: values.authHeaderName } : {}),
    });
  };

  // Afirmação estrutural sobre outro app (convenção 13), lida em
  // apps/api/src/Buteco.Api/McpServers/Entities/McpServer.cs:63-78 (UpdateDetails
  // limpa a credencial quando o tipo muda, `authTypeChanged`, :74-76) e em
  // apps/api/src/Buteco.Api/McpServers/Commands/UpdateMcpServer/UpdateMcpServerCommandHandler.cs:33-36
  // (sem credencial para o tipo novo, 400 antes de salvar). GATILHO: o update
  // voltar a manter a credencial na troca entre tipos com credencial torna
  // "não é reaproveitada" falsa.
  const typeChangeWarning =
    typeChanged && authType !== 'None' && savedAuthType !== 'None' ? (
      <Alert color="yellow" mb={8} data-testid="type-change-warning">
        <Text size="sm">
          O tipo mudou de {authTypeLabels[savedAuthType]} para {authTypeLabels[authType]}. Informe a
          credencial nova: a credencial salva era do tipo anterior e não é reaproveitada.
        </Text>
      </Alert>
    ) : null;

  return (
    <form onSubmit={form.onSubmit(handleSubmit)}>
      <Paper withBorder radius="md" p="lg">
        <Stack>
          <TextInput
            label="Nome"
            placeholder="Nome do servidor MCP"
            withAsterisk
            {...form.getInputProps('name')}
            error={fieldError('name')}
          />
          <Textarea
            label="Descrição"
            placeholder="Descrição do servidor MCP"
            rows={4}
            resize="vertical"
            {...form.getInputProps('description')}
            error={fieldError('description')}
          />
          <TextInput
            label="Url"
            placeholder="https://mcp.exemplo.com/sse"
            withAsterisk
            {...form.getInputProps('url')}
            error={fieldError('url')}
          />

          <Divider mt={4} />
          <SectionLabel>Autenticação</SectionLabel>

          <Radio.Group
            label="Tipo de autenticação"
            withAsterisk
            value={authType}
            onChange={(value) => form.setFieldValue('authType', value as McpServerAuthType)}
            error={fieldError('authType')}
          >
            <SimpleGrid type="container" cols={cardsColumns} spacing={12} mt={4}>
              {authTypeOrder.map((type) => (
                <RadioCardOption
                  key={type}
                  value={type}
                  title={authTypeLabels[type]}
                  description={authTypeDescriptions[type]}
                />
              ))}
            </SimpleGrid>
          </Radio.Group>

          {/* Afirmação estrutural sobre outro app (convenção 13), lida em
              apps/api/src/Buteco.Api/McpServers/Entities/McpServer.cs:74-76
              (o update apaga a credencial quando o tipo vai para None).
              GATILHO: o update deixar de limpar a credencial ao ir para None
              torna "é apagada" falsa. */}
          {typeChanged && authType === 'None' && (
            <Alert color="yellow" data-testid="credential-removal-warning">
              <Text size="sm">
                Ao salvar, a credencial atual ({authTypeLabels[savedAuthType]}) é apagada. Ela não
                fica guardada sem uso.
              </Text>
            </Alert>
          )}

          {isCustomHeader && (
            <TextInput
              label="Nome do header"
              description="O header em que o serviço espera o token, como está na documentação dele."
              placeholder="X-API-TOKEN"
              withAsterisk
              autoComplete="off"
              spellCheck={false}
              styles={{ input: { fontFamily: 'var(--mantine-font-family-monospace)' } }}
              {...form.getInputProps('authHeaderName')}
              error={fieldError('authHeaderName')}
            />
          )}

          {authType !== 'None' && (
            <PasswordInput
              label={isCustomHeader ? 'Token' : 'Credencial'}
              placeholder={
                keepsSavedCredential
                  ? 'Deixe em branco para manter a credencial atual'
                  : 'Credencial de acesso'
              }
              description={
                keepsSavedCredential
                  ? 'Deixe em branco para manter a credencial atual.'
                  : 'Enviada cifrada (AES-GCM) e nunca retorna na API.'
              }
              withAsterisk={!keepsSavedCredential}
              // O aviso de troca de tipo fica DENTRO do campo, entre a descrição
              // e a caixa: a descrição do Mantine é um <p>
              // (InputDescription.mjs:34), que não pode conter o Alert.
              inputContainer={(children) => (
                <>
                  {typeChangeWarning}
                  {children}
                </>
              )}
              {...form.getInputProps('credential')}
              error={fieldError('credential')}
              // O PasswordInput do Mantine 9.4.2 não marca o <input> com
              // aria-invalid: chama o Input com `withAria: false`
              // (@mantine/core/esm/components/PasswordInput/PasswordInput.mjs:159)
              // e põe só `data-invalid` no <input> (:165). As props que sobram
              // chegam ao <input> pelo `...rest` (:171), então o atributo vai
              // explícito, como o TextInput já faz sozinho (Input.mjs:75).
              aria-invalid={fieldError('credential') ? true : undefined}
            />
          )}

          {/* Afirmação estrutural sobre outro app (convenção 13), lida em
              apps/api/src/Buteco.Api/McpServers/Connectivity/McpConnectionTester.cs:126-140
              e apps/workers/src/Buteco.Workers/Mcp/McpTransportFactory.cs:68-82:
              os dois montam `AdditionalHeaders` com `[nome] = credencial`, cru,
              sem prefixo e sem Authorization (BearerToken é o único que monta
              `Authorization: Bearer`), e o SDK manda AdditionalHeaders em toda
              requisição da sessão (medido na V1 de
              openspec/changes/archive/2026-10-18-mcp-autenticacao-header-customizado/design.md).
              Vale também para as descrições dos cartões BearerToken e
              CustomHeader acima. GATILHO: a montagem do header mudar em
              qualquer dos dois apps, ou a #161 trocar a forma (token obtido e
              renovado), torna esta caixa falsa. O valor do token nunca entra
              aqui (decisão 2 do dono na #168). */}
          {isCustomHeader && (
            <Box
              px="sm"
              py={10}
              bg="var(--buteco-surface-subtle)"
              style={{
                borderRadius: 'var(--mantine-radius-sm)',
                border: '1px solid var(--mantine-color-default-border)',
              }}
              data-testid="credential-preview"
            >
              <Stack gap={5}>
                <Text size="xs" c="dimmed">
                  Como a credencial sai em cada requisição ao servidor
                </Text>
                <Text size="sm" ff="monospace" style={{ wordBreak: 'break-all' }}>
                  {form.values.authHeaderName.trim() || '<nome do header>'}: ••••••••
                </Text>
                <Text size="xs" c="dimmed">
                  O valor vai como foi digitado, sem prefixo. Authorization não é enviado.
                </Text>
              </Stack>
            </Box>
          )}

          {testMode === 'saved' && (
            <Text size="xs" c="dimmed" data-testid="saved-credential-test-note">
              Sem digitar a credencial, o teste usa a configuração salva deste servidor
              {isCustomHeader
                ? ', inclusive o nome do header: uma alteração no nome só é testada depois de salvar.'
                : '.'}
            </Text>
          )}
          {testMode === 'blocked' && (
            <Text size="xs" c="dimmed" data-testid="blocked-test-note">
              Para testar o tipo novo, informe a credencial nova.
            </Text>
          )}

          {testOutcome?.kind === 'result' && (
            <ConnectionTestResultAlert result={testOutcome.result} />
          )}
          {/* Afirmação estrutural sobre outro app (convenção 13), lida em
              apps/api/src/Buteco.Api/McpServers/Endpoints/McpServerEndpoints.cs:151-155:
              o 400 do teste não salvo sai de ValidateTestShape, antes do
              `mediator.Send` que conecta. GATILHO: a validação de forma do
              teste passar a rodar depois de conectar torna "Nenhuma conexão
              foi tentada" falsa. */}
          {testOutcome?.kind === 'invalid' && (
            <Alert color="red" data-testid="connection-test-invalid">
              <Stack gap={4}>
                <Text size="sm">
                  Corrija os campos destacados antes de testar. Nenhuma conexão foi tentada.
                </Text>
                {testOutcome.unmatchedMessages.map((message) => (
                  <Text key={message} size="sm">
                    {message}
                  </Text>
                ))}
              </Stack>
            </Alert>
          )}

          <Group>
            <Button type="submit" loading={submitting}>
              {submitLabel}
            </Button>
            <Button type="button" variant="default" onClick={onCancel}>
              Cancelar
            </Button>
            <Button
              type="button"
              variant="outline"
              onClick={handleTestConnection}
              disabled={testMode === 'blocked'}
              loading={testMutation.isPending || testSavedMutation.isPending}
            >
              Testar conexão
            </Button>
          </Group>
        </Stack>
      </Paper>
    </form>
  );
}
