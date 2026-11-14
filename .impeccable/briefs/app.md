# Surface brief: LogPilot web app (all routes)

Mode: Operate. Audience: the team's own developers/support engineers. Job: see failures, ask why, verify in raw logs.
Constraints: keep page structure, navigation and features; MUI standard controls; English copy; offline (system fonts).

## Direction contract
THESIS: An investigation console where the answer's route is visible. It refuses the default "SaaS dashboard" (indigo accent,
same-size white cards, emoji empty states) and the opposite costume (black terminal, neon green).
OWN-WORLD: Restrained palette on a teal-tinted slate. A darker rail carries the shell; panels are flat with 1px hairlines,
8px radius, no shadows at rest. One accent, signal blue, only for primary actions, selection, links and focus. Red / amber /
green appear only as state. Type: system UI sans for everything people read; system monospace only for data (error codes, ids,
timestamps, log text, JSON); tabular numerals; 1.2 type scale; dense rows.
STORY: An engineer opens the dashboard, sees which error code is rising, presses "Ask assistant" next to it, and reads an answer
whose route through the log tables is drawn like a transit line, with the evidence one click away.
FIRST VIEWPORT: Rail (product name, nav, theme switch). Dashboard: header with range switch; one compact status strip of six
figures; error trend (2/3) beside errors by service (1/3); then top error codes and recent errors, each row with an ask action.
FORM: Operate console, own list position 1. seed: none (unavailable) — the impeccable launcher was not run (binary download refused); direction pinned by the user ("A — sakin konsol").
SIGNATURE: The investigation route line — collections as stops, join keys as segment labels, a hollow stop where nothing was found.
FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance
