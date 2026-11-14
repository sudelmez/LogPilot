---
name: LogPilot
description: Quiet console for an engineering team — see what is failing, ask why, verify in the logs.
colors:
  dark-ground: "#0E1416"
  dark-rail: "#0A1012"
  dark-panel: "#131B1E"
  dark-raised: "#1A2428"
  dark-line: "#243236"
  dark-text: "#E4ECEE"
  dark-text-muted: "#9BAEB3"
  dark-text-faint: "#5E7176"
  dark-accent: "#7AB0FF"
  dark-error: "#F2686F"
  dark-warning: "#E9A63B"
  dark-success: "#4EC28A"
  light-ground: "#F3F6F6"
  light-rail: "#E6ECEC"
  light-panel: "#FFFFFF"
  light-raised: "#F0F4F4"
  light-line: "#D5DEDF"
  light-text: "#0F1A1D"
  light-text-muted: "#4C5F64"
  light-text-faint: "#8A9A9E"
  light-accent: "#1F5FBF"
  light-error: "#C2333B"
  light-warning: "#A86A07"
  light-success: "#1E7F4F"
typography:
  page-title:
    fontFamily: "-apple-system, BlinkMacSystemFont, 'Segoe UI Variable Text', 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif"
    fontSize: "1.375rem"
    fontWeight: 650
    lineHeight: 1.2
    letterSpacing: "-0.015em"
  panel-title:
    fontFamily: "-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif"
    fontSize: "0.9375rem"
    fontWeight: 600
    lineHeight: 1.4
  figure:
    fontFamily: "-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif"
    fontSize: "1.625rem"
    fontWeight: 650
    lineHeight: 1.25
    letterSpacing: "-0.02em"
  body:
    fontFamily: "-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif"
    fontSize: "0.8125rem"
    fontWeight: 400
    lineHeight: 1.5
  label:
    fontFamily: "-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif"
    fontSize: "0.75rem"
    fontWeight: 600
    lineHeight: 1.45
  data:
    fontFamily: "ui-monospace, 'SF Mono', SFMono-Regular, Menlo, Consolas, monospace"
    fontSize: "0.75rem"
    fontWeight: 600
    lineHeight: 1.6
rounded:
  code: "4px"
  control: "6px"
  panel: "8px"
spacing:
  xs: "4px"
  sm: "8px"
  md: "16px"
  lg: "24px"
components:
  button-primary:
    backgroundColor: "{colors.dark-accent}"
    textColor: "{colors.dark-ground}"
    rounded: "{rounded.control}"
    height: "32px"
    padding: "0 12px"
  panel:
    backgroundColor: "{colors.dark-panel}"
    rounded: "{rounded.panel}"
    padding: "16px"
  code-error:
    backgroundColor: "color-mix(in srgb, #F2686F 12%, transparent)"
    textColor: "{colors.dark-error}"
    typography: "{typography.data}"
    rounded: "{rounded.code}"
    padding: "0 6px"
  nav-item-selected:
    backgroundColor: "color-mix(in srgb, #7AB0FF 14%, transparent)"
    textColor: "{colors.dark-text}"
    rounded: "{rounded.control}"
    height: "34px"
---

# LogPilot design system

## Overview
An Operate-mode internal tool for the engineering team: an investigation console where the answer's route through the log
tables is visible. Dark-first for long sessions next to an IDE, with an equal light scheme; the user's choice (system, light,
dark) is remembered. Expression is restraint: one accent, state colour only for state, monospace only for data. The one
signature is the **investigation route line** — collections as stops, join keys as labels, a hollow stop where nothing was found.

## Colors
Restrained strategy on a teal-tinted slate. The shell rail is one step darker (dark) or tinted (light) than the content
ground; panels sit on the ground with a 1px line. **Signal blue** (`dark-accent` / `light-accent`) is the only accent: primary
actions, selection, links, focus ring, the route line. Red / amber / green appear only as error / warning / success and in
status dots, error-code tags, chart series. Never decorate with them. Selection highlight is the accent at 32%.

## Typography
One sans family (the platform UI face) for everything people read, on a fixed ~1.2 rem scale: page title 22px, panel title
15px, body 13px, labels 12px, figures 26px. Monospace is reserved for data — error codes, ids (COR-, TKL-, POL-, PRP-, PAY-),
timestamps in tables, table names, raw JSON — never as a "technical" costume. Numerals are tabular everywhere.

## Layout
216px rail (collapses to a top bar + drawer below 900px) and a content column capped at 1440px. Pages: one h1 header with
page-level actions on the right, then panels in a 16px-gap grid. Dashboard opens with a single status strip (six figures on one
surface, separated by 1px lines) instead of six cards. Tables are dense (7px row padding); long text cells take the remaining
width and ellipsize so action columns stay visible. The assistant is a two-pane workspace (conversations | thread) with a
920px reading column and a docked composer.

## Elevation & Depth
Flat. No shadows at rest; separation comes from the ground/panel/raised steps and 1px lines. Only floating layers (tooltip,
menus, drawers) carry a soft offset shadow.

## Shapes
8px panels, 6px controls and nav items, 4px code tags, fully round status dots and route stops.

## Components
- **StatusDot**: 7px dot + label for severity, health, incident status, confidence. Critical/unhealthy adds a 3px halo and bold label.
- **Code**: monospace tag for data; error tone on a 12% tint of the state colour; neutral on `raised`. Can be a link.
- **AskButton**: text button with a chat icon; opens the assistant with a prepared Turkish question (prefilled, not sent).
- **RouteLine**: horizontal stops on desktop, vertical on phones; filled stop = found, hollow = nothing found; table names
  break at underscores; descriptions clamp to 2–3 lines with the full text in a tooltip.
- **Panel**: card with an inline title + caption row; tables inside panels run edge to edge.
- **RangeToggle**: segmented 15m / 1h / 6h / 24h / 7d with full names in tooltips.

## Do's and Don'ts
- Do let red/amber/green mean state and nothing else; do keep the accent for actions and selection.
- Do show data in monospace and prose in sans; do keep numbers tabular.
- Do keep every chart unanimated (it refreshes every 15 s) and every interaction under 250 ms.
- Don't use kickers/eyebrows above headings, gradient text, glass, side stripes, emoji or glyph icons.
- Don't add cards inside cards or same-size icon cards as page structure.
- Don't let a table push its action column off screen; ellipsize the free-text column instead.
