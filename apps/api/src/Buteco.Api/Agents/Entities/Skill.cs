namespace Buteco.Api.Agents.Entities;

// Shape provisório (Decision 3 do design.md da change
// backend-agente-description-skills) — deliberadamente mais simples que o
// AgentSkill do protocolo A2A. O mapeamento para o shape exigido pelo
// protocolo fica inteiramente para a change backend-a2a-agent-card.
public sealed record Skill(string Name, string? Description);
