# Ticket Authoring Template

> Audience: agent. Ticket rules: [Ticket workflow](README.md#ticket-workflow).

Procedure for a new ticket. Replace the angle-bracket placeholders.

1. Look up the card ID: `op_find_card_work_package(card_id, project_id=4)`. It resolves legacy `WI-...` card IDs through description provenance and the mapping table, so renamed subjects still match. If a ticket exists, do not create another. Append the mapping row only when it is missing.
2. Create the ticket in project Panopticon (id 4) with `op_create_work_package`. Set the subject to the descriptive title only (for example `Wire the parser`), with no `WI-...` prefix. Do not allocate a `WI-...` ID for a new ticket. The native work-package ID assigned at creation is the canonical reference for the ticket. Put the ticket body in the OpenProject description: roadmap phase link, type, effort (`S`), diagrams link, goal, context to read, steps, files likely to change, acceptance criteria, validation commands, and out-of-scope guardrails. Steps must be explicit enough for a low-effort model to follow literally. Split or escalate work that needs deep design reasoning.
3. Add each prerequisite with `op_create_work_package_relation(wp_id, to_id, type="follows")`.
4. Only when reconciling a card file the branch carried, replace that card file with a row appended to [`legacy-card-mapping.md`](legacy-card-mapping.md), keeping the table sorted by card ID:

```markdown
| `WI-<PHASE>-<NN>` |  | `<card filename>.md` | <short title> | https://projects.reid-corp.com/work_packages/<id> |
```

`<card filename>.md` is the card file the branch carried. A ticket that replaces a split card fills the `Original card ID` column with the pre-split ID. A row holds no status, no `Result:` line, and no body text. A new ticket with no card file adds no mapping row.
