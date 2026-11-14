# LogPilot — product context

## What it is
An internal tool for the engineering team to see what is failing in the company's application, quote, SMS and policy-purchase
systems, and to ask an AI assistant *why* instead of digging through logs by hand. All data in this repository is synthetic.

## Who uses it, where
Developers and support engineers of the team itself (not business units). They keep it open next to their IDE and terminal for
long stretches, often in dimmed rooms, switching between "something is red" and "find out why". Desktop first; phone is for a quick look.

## Jobs, in order of frequency
1. Notice what is failing now: error trend, top error codes, unhealthy services, active incidents.
2. Ask the assistant why a specific error, user, quote, policy or SMS failed — and trust the answer because it shows the evidence
   and the path it followed through the log tables.
3. Drill into raw logs (filters, correlation id, raw JSON/XML) when the answer needs checking.
4. Manage incidents and record verified fixes (knowledge base); trigger synthetic scenarios.

## Brand commitments
- Internal and honest: no marketing tone, no decoration that does not carry information.
- Evidence over claims: every answer links to the logs it used; confidence is computed, not asserted.
- Personal data stays masked everywhere.

## Constraints
React 19 + MUI 9 + Recharts; English UI copy (questions to the assistant are usually Turkish); runs offline (no web fonts).
