---
name: resolve-pr-comments
description: Validate and resolve PR review comments by creating failing tests first or direct minor edits.
user-invoked: true
disable-model-invocation: true
---

# Resolve PR Comments

You are resolving review comments on a Pull Request. Never guess fixes. Convert logic feedback into verifiable checks before editing code.

## Phase 1: Classify & Scope (The Adaptive Gate)

Categorize each comment:
1. **Logic / Bug / Edge Case**: Build a single failing test (RED) asserting the exact issue.
2. **Code Standard / Structural Smell**: Add or trigger an automated linter/type check that fails.
3. **Minor / Doc / Typo Fix**: Skip the Red step. Proceed directly to Phase 2.
4. **Major Scope Creep (Architectural Redesign)**: STOP. Advise the user to run `/grill-with-docs` instead of fixing inline.

**GATE**: For Categories 1 & 2, run the test/check command. If it does not fail (RED), STOP and ask the user for clarification.

## Phase 2: Resolve via TDD

Call the Skill tool with "tdd".
1. Write the minimal implementation code to turn the failing test or check GREEN.
2. Run typecheck and the specific test file to confirm zero regressions.
3. Commit per resolved comment using a clear message (e.g., `fix(pr-review): handle null case in cart total`).

## Phase 3: Re-review & PR Update

1. Do NOT review code in this authoring session to avoid confirmation bias.
2. Call the Skill tool with "pr" to regenerate the PR body.
3. Prompt the user: *"Fixes committed and PR body updated. Type `/clear` and run `/code-review origin/main...HEAD` in a fresh session for an independent pass."*
