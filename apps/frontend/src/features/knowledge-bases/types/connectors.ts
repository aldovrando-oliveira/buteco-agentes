// Formato de fio das duas rotas do apps/connectors que o operador acessa
// (connectors-api, D9 da change apps-connectors-google-drive): camelCase, enum
// como string.

// GET /connectors/providers — só os provedores configurados, ordenados por `key`.
export interface ConnectorProvider {
  key: string;
  accountEmail: string;
}

export type ConnectorFolderKind = 'SharedDrive' | 'Folder';

// GET /connectors/providers/{providerKey}/folders?parentId= — ordenadas por
// `name` (ordinal) e desempate por `id`.
export interface ConnectorFolder {
  id: string;
  name: string;
  kind: ConnectorFolderKind;
  webUrl: string;
}
