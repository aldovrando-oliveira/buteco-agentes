---
name: openspec-sync-specs
description: Sync delta specs from a change to main specs. Use when the user wants to update main specs with changes from a delta spec, without archiving the change.
license: MIT
compatibility: Requires openspec CLI.
metadata:
  author: openspec
  version: "1.0"
  generatedBy: "1.4.1"
---

Sync delta specs from a change to main specs.

This is an **agent-driven** operation - you will read delta specs and directly edit main specs to apply the changes. This allows intelligent merging (e.g., adding a scenario without copying the entire requirement).

**Input**: Optionally specify a change name. If omitted, check if it can be inferred from conversation context. If vague or ambiguous you MUST prompt for available changes.

**Steps**

1. **If no change name provided, prompt for selection**

   Run `openspec list --json` to get available changes. Use the **AskUserQuestion tool** to let the user select.

   Show changes that have delta specs (under `specs/` directory).

   **IMPORTANT**: Do NOT guess or auto-select a change. Always let the user choose.

2. **Resolve change context**

   Run:
   ```bash
   openspec status --change "<name>" --json
   ```

   If status reports `actionContext.mode: "workspace-planning"`, explain that workspace spec sync is not supported in this slice and STOP. Do not fall back to repo-local paths or edit linked repos.

3. **Find delta specs**

   Use `artifactPaths.specs.existingOutputPaths` from the status JSON as the list of delta spec files.

   Each delta spec file contains sections like:
   - `## ADDED Requirements` - New requirements to add
   - `## MODIFIED Requirements` - Changes to existing requirements
   - `## REMOVED Requirements` - Requirements to remove
   - `## RENAMED Requirements` - Requirements to rename (FROM:/TO: format)

   If no delta specs found, inform user and stop.

4. **For each delta spec, apply changes to main specs**

   For each repo-local capability delta spec path returned by the CLI:

   a. **Read the delta spec** to understand the intended changes

   b. **Read the main spec** at `openspec/specs/<capability>/spec.md` (may not exist yet)

   c. **Apply changes intelligently**:

      **ADDED Requirements:**
      - If requirement doesn't exist in main spec → add it
      - If requirement already exists → update it to match (treat as implicit MODIFIED)

      **MODIFIED Requirements:**
      - Find the requirement in main spec
      - Apply the changes - this can be:
        - Adding new scenarios (don't need to copy existing ones)
        - Modifying existing scenarios
        - Changing the requirement description
      - Preserve scenarios/content not mentioned in the delta

      **REMOVED Requirements:**
      - Remove the entire requirement block from main spec

      **RENAMED Requirements:**
      - Find the FROM requirement, rename to TO

      **`## Purpose` section (if the delta has one):**
      - The delta may carry a `## Purpose` section above its requirement
        sections. It is content, not a heading to skip.
      - If the main spec's Purpose is a `TBD - ...` placeholder → **replace it**
        with the delta's Purpose.
      - If the main spec has a real Purpose and the delta carries one → the
        delta's wins (the change author just touched this capability).
      - If the delta has no `## Purpose` → leave the main spec's Purpose alone.
      - Never overwrite a real Purpose with a placeholder.

   d. **Create new main spec** if capability doesn't exist yet:
      - Create `openspec/specs/<capability>/spec.md`
      - **Purpose section: use the delta's `## Purpose` verbatim when it has
        one.** Only when the delta carries no Purpose at all, write the
        placeholder `TBD - defined by change <change-name>. Update Purpose after
        archive.` and tell the user, in the summary, that this capability needs
        a Purpose written by whoever touched the code.
      - Add Requirements section with the ADDED requirements

5. **Verify what landed in the main specs — not what you intended to write**

   Re-read each main spec you touched and check, before reporting success:

   - **No `TBD - ` placeholder in a capability whose delta carried a
     `## Purpose`.** This is the check that matters most: `openspec validate
     --specs --strict` does **not** catch it, because a placeholder is valid
     text. 39 of this repo's 44 capabilities carry one and all 44 validate.
   - **No duplicated requirement block** — grep the `### Requirement:` titles
     and confirm there are no repeats.
   - **No leaked delta headings** — `## ADDED Requirements`,
     `## MODIFIED Requirements`, `## REMOVED Requirements` and
     `## RENAMED Requirements` belong to the delta and must never appear in a
     main spec.
   - **Nothing lost.** For a MODIFIED delta, the diff against the main spec as
     it was before the sync should be *additive* unless the delta deliberately
     rewrote or removed something. Snapshot the file before editing if that
     makes the check easier.
   - Then run `openspec validate --specs --strict` — necessary, not sufficient,
     for the reasons above.

6. **Show summary**

   After applying all changes, summarize:
   - Which capabilities were updated
   - What changes were made (requirements added/modified/removed/renamed)
   - Which capabilities got their Purpose from the delta, and which were left
     with a placeholder needing one
   - The result of each verification in step 5

**Delta Spec Format Reference**

```markdown
## ADDED Requirements

### Requirement: New Feature
The system SHALL do something new.

#### Scenario: Basic case
- **WHEN** user does X
- **THEN** system does Y

## MODIFIED Requirements

### Requirement: Existing Feature
#### Scenario: New scenario to add
- **WHEN** user does A
- **THEN** system does B

## REMOVED Requirements

### Requirement: Deprecated Feature

## RENAMED Requirements

- FROM: `### Requirement: Old Name`
- TO: `### Requirement: New Name`
```

**Key Principle: Intelligent Merging**

Unlike programmatic merging, you can apply **partial updates**:
- To add a scenario, just include that scenario under MODIFIED - don't copy existing scenarios
- The delta represents *intent*, not a wholesale replacement
- Use your judgment to merge changes sensibly

**Output On Success**

```
## Specs Synced: <change-name>

Updated main specs:

**<capability-1>**:
- Added requirement: "New Feature"
- Modified requirement: "Existing Feature" (added 1 scenario)

**<capability-2>**:
- Created new spec file
- Added requirement: "Another Feature"

Main specs are now updated. The change remains active - archive when implementation is complete.
```

**Guardrails**
- Read both delta and main specs before making changes
- Preserve existing content not mentioned in delta
- **A real `## Purpose` is never replaced by a placeholder** — not on create, not
  on update. A capability's Purpose says which question it answers, and that is
  the one thing a reader cannot recover from the scenarios.
- If something is unclear, ask for clarification
- Show what you're changing as you go
- Verify the main specs after writing them (step 5); reporting the intent is not
  the same as checking the result
- The operation should be idempotent - running twice should give same result

**Known second producer of placeholders (not this skill)**

The `openspec` CLI creates a main spec on its own when a change is archived
**without** a prior sync — `dist/core/specs-apply.js` writes
`TBD - created by archiving change <name>. Update Purpose after archive.`. Note
the different wording: this repo's 39 placeholders all say *"defined by change"*,
so none of them came from that path, but the archive flow does offer "Archive
without syncing" and it stays reachable. Syncing before archiving keeps the
Purpose under this skill's control; when grepping for the placeholder stock,
search both `TBD - defined by change` and `TBD - created by archiving change`.
