# PineGlow Launcher — locked visual system (松光启动器)

Source: values supplied by the user in the brief. No guessing — hex values are
authoritative and reproduced verbatim. OKLch values were computed from those hex
values and are used only to derive interaction-state colors.

One sentence: a dark evergreen "night forest" launcher where mint green is the
single accent, surfaces are cool near-black teals, and every error is spoken with
a warm coral that never reads as alarm-red.

## Core tokens (six)

| Token | Hex | OKLch |
|---|---|---|
| `--bg` | `#10191b` | `oklch(20.5% 0.014 212.9)` |
| `--surface` | `#172326` | `oklch(24.6% 0.018 214.9)` |
| `--fg` | `#edf3ef` | `oklch(95.8% 0.008 157.1)` |
| `--muted` | `#a3b5b7` | `oklch(76.0% 0.020 204.8)` |
| `--border` | `#2c3e42` | `oklch(35.0% 0.024 212.9)` |
| `--accent` | `#91d5af` | `oklch(81.7% 0.087 159.2)` |

## Supporting tokens

| Token | Hex | OKLch | Role |
|---|---|---|---|
| `--sidebar` | `#0d1517` | `oklch(18.9% 0.013 214.8)` | left navigation rail |
| `--danger` | `#ff8e86` | `oklch(76.7% 0.138 25.0)` | errors / failed requests |
| `--warn` | `#eac585` | `oklch(84.1% 0.092 80.7)` | actionable / needs attention |

Derived state colors are generated with `color-mix(in oklch, …)` from these
tokens, never from invented hex.

## Type

- Display / UI: `"Segoe UI", "Microsoft YaHei UI", system-ui, sans-serif`
- Mono (technical detail, version tags): `"Cascadia Code", "Consolas", ui-monospace, monospace`
- Chinese-first rendering: Microsoft YaHei UI carries CJK; Segoe UI carries Latin.
  The stack is one family for a utilitarian, data-dense tool surface — this is the
  sanctioned exception to the "two typefaces" rule.

## Geometry

- Card radius 14px, button radius 8px, chip radius 999px.
- Sidebar rail 208px; content gutter 24px; card padding 16–20px.
- Focus ring: 2px `--accent` with 2px offset, on `:focus-visible` only.

## Observed rules (the visual language)

1. **One accent, used twice per screen at most** — mint `--accent` marks the single
   primary action and the active nav item. Everything else is neutral surface.
2. **Errors stay warm, never red-alarm** — coral `--danger` for failure, amber
   `--warn` for "needs your attention", `--accent` for "resolved".
3. **Status is a chip, not a wall of color** — state is communicated by a small
   icon + label chip plus one line of plain-language copy; raw technical data is
   always tucked behind an expandable details control.
4. **Flat, cool, quiet** — no gradients across surfaces, no glow, no texture. Depth
   comes from a single hairline `--border` and a one-step surface lift.
5. **The interface never pretends** — prototype/simulated data is labeled, and a
   failure never renders an impossible value (no `0001-01-01`, no fake "0 items"
   when the request actually failed).

