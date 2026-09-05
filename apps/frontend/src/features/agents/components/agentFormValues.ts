// Estado do formulário de agente: string em todos os campos de texto
// (inputs controlados); a conversão para `null` acontece só no submit
// (Decision 2 do design.md da change frontend-agente-description-skills).
// Vive em arquivo próprio para AgentForm e AgentSkillsFields compartilharem
// o tipo sem import circular.
export interface AgentSkillFormValue {
  name: string;
  description: string;
}

export interface AgentFormValues {
  name: string;
  description: string;
  instructions: string;
  provider: string;
  model: string;
  skills: AgentSkillFormValue[];
}
