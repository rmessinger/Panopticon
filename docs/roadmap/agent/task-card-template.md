# Ticket Authoring Template

> Audience: agent. Ticket rules: [Ticket workflow](README.md#ticket-workflow).

Procedure for a new ticket. Replace the angle-bracket placeholders.

1. Look up the card ID: `op_find_card_work_package(card_id, project_id=4)`. If a ticket exists, do not create another. Add the stub only when it is missing.
2. Create the ticket in project Panopticon (id 4) with `op_create_work_package`. Set the subject to `<CARD-ID>: <title>` (for example `WI-C1-01: Title`). `op_find_card_work_package` matches subjects that start with the card ID followed by a non-ID delimiter, so this format is required. Put the ticket body in the OpenProject description: roadmap phase link, type, effort (`S`), diagrams link, goal, context to read, steps, files likely to change, acceptance criteria, validation commands, and out-of-scope guardrails. Steps must be explicit enough for a low-effort model to follow literally. Split or escalate work that needs deep design reasoning.
3. Add each prerequisite with `op_create_work_package_relation(wp_id, to_id, type="follows")`.
4. Add the stub `cards/WI-<PHASE>-<NN>-<slug>.md`:

```markdown
# Task Card `WI-<PHASE>-<NN>`: <short title>

Ticket: https://projects.reid-corp.com/work_packages/<id>

Canonical card ID: `WI-<PHASE>-<NN>`

This file is a link stub. The OpenProject work package is the canonical ticket: status, prerequisites, description, and completion records live there, not in this file.
```

A ticket that replaces a split card adds an `Original card ID:` line with the pre-split ID after the canonical line. The stub holds no status, no `Result:` line, and no body text.
