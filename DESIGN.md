---
name: MyCRMRecords
description: Companies, territories and CRM users for the sales team, on calm cards under floating glass controls.
colors:
  page: "#f2f2f7"
  page-dark: "#000000"
  card: "#ffffff"
  card-dark: "#1c1c1e"
  surface: "#f7f8fb"
  surface-2: "#eef0f4"
  ink: "#111114"
  ink-dark: "#f5f5f7"
  ink-2: "#454a54"
  ink-2-dark: "#b4b4bd"
  muted: "#5a5f6b"
  placeholder: "#676c77"
  placeholder-dark: "#8e8e96"
  accent: "#0a58f0"
  accent-dark-theme: "#3d82ff"
  accent-fill-dark: "#246bec"
  accent-ink-dark: "#6a9fff"
  teal: "#0d9488"
  teal-dark: "#2dd4bf"
  coral: "#f2675a"
  coral-dark: "#ff8a7e"
  hue-violet: "#8b5cf6"
  hue-purple: "#7c3aed"
  hue-amber: "#e59a0b"
  hue-red: "#d9443a"
  hue-orange: "#c2410c"
  hue-slate: "#64748b"
  hue-slate-dark: "#475569"
  hue-grey: "#8e8e93"
  hue-cyan: "#0891b2"
  hue-lavender: "#7c6ad9"
typography:
  page-title:
    fontFamily: "Inter, -apple-system, BlinkMacSystemFont, Segoe UI, Roboto, sans-serif"
    fontSize: "2rem"
    fontWeight: 800
    lineHeight: 1.08
    letterSpacing: "-0.03em"
  card-title:
    fontFamily: "Inter, -apple-system, BlinkMacSystemFont, Segoe UI, Roboto, sans-serif"
    fontSize: "1.0625rem"
    fontWeight: 700
    lineHeight: 1.3
  body:
    fontFamily: "Inter, -apple-system, BlinkMacSystemFont, Segoe UI, Roboto, sans-serif"
    fontSize: "0.9375rem"
    fontWeight: 400
    lineHeight: 1.45
    fontFeature: "\"cv11\", \"ss03\", \"tnum\""
  label:
    fontFamily: "Inter, -apple-system, BlinkMacSystemFont, Segoe UI, Roboto, sans-serif"
    fontSize: "0.8125rem"
    fontWeight: 600
  tab-label:
    fontFamily: "Inter, -apple-system, BlinkMacSystemFont, Segoe UI, Roboto, sans-serif"
    fontSize: "0.6875rem"
    fontWeight: 600
rounded:
  tile: "9px"
  chip: "12px"
  control: "18px"
  capsule: "22px"
  card: "24px"
  pill: "999px"
spacing:
  card-x: "24px"
  card-y: "22px"
  gutter: "16px"
  gap: "12px"
components:
  button-primary:
    backgroundColor: "{colors.accent}"
    textColor: "#ffffff"
    rounded: "{rounded.capsule}"
    height: "46px"
    padding: "0 20px"
  button-primary-dark:
    backgroundColor: "{colors.accent-fill-dark}"
    textColor: "#ffffff"
  tab-selected:
    backgroundColor: "{colors.accent}"
    textColor: "#ffffff"
    rounded: "{rounded.capsule}"
  tab-selected-dark:
    backgroundColor: "{colors.accent-fill-dark}"
    textColor: "#ffffff"
  card:
    backgroundColor: "{colors.card}"
    rounded: "{rounded.card}"
    padding: "22px 24px"
  chip-salesperson:
    textColor: "{colors.accent}"
    rounded: "{rounded.chip}"
    height: "24px"
    padding: "0 10px"
  section-tile:
    textColor: "#ffffff"
    rounded: "{rounded.tile}"
    size: "30px"
---

# Design System: MyCRMRecords

The tokens live in `src/MyCRMRecords.Web/app.css`. The values above are the effective ones: the light set and, with a
`-dark` suffix, the dark set (`prefers-color-scheme: dark`). This file describes what is there and the rules that keep it
coherent; when the two disagree, fix whichever is wrong in the same pull request.

## Overview

**Creative North Star: "The Glass Ledger"**

MyCRMRecords is a working tool for a sales team: the companies they look after, who owns which territory, and the
people behind the accounts. The records are the point, so they sit on calm, solid white cards on a light grey page,
dense enough to scan a list of fifty companies but never cramped. Everything you *act* on floats above them in frosted
glass: the top bar, the phone's bottom bar, buttons, tab and filter capsules, search, menus, dialogs and messages.

The look is deliberately close to the phone the salespeople carry (iOS: system greys, one blue accent, round capsules,
a bottom tab bar and a More sheet on a phone), so nothing needs learning. Personality is in precise details, not decoration:
the spring of a pressed button, figures that line up in columns, a tile colour per kind of record.

**Key Characteristics:**
- Content is solid; controls are glass. Never the other way round.
- One accent (blue) for action, selection and links. Other hues name a kind of record, never an action.
- Phone first: below 56.25em the menu becomes a floating bottom bar, tables become cards, sections open full screen.
- Light and dark from the device's setting; every colour has both.

## Colors

A neutral iOS-like grey page with one saturated blue, plus a small set of hues that name kinds of records.

### Primary
- **Action Blue** (`--accent`, #0a58f0; dark #3d82ff): links, selected text, focus rings, the selected item's tint
  (`--accent-light`).
- **Action Blue, filled** (`--accent-fill`, #0a58f0; dark #246bec): the solid fill behind white text: primary buttons,
  the selected tab or toggle, today's date. Darker in dark mode so white text stays above 4.5:1.
- **Action Blue, as text** (`--accent-ink`, the accent; dark #6a9fff): blue text in dark mode, where the plain accent is
  too dark on the near-black cards. Use it for any `color:` that would be the accent.

### Secondary
- **Ledger Teal** (`--teal`, #0d9488; dark #2dd4bf): black copies on charts, machines, team tags.
- **Signal Coral** (`--coral`, #f2675a; dark #ff8a7e): colour copies on charts, late, lost, no salesperson, delete.
- **Teal / Coral, as text** (`--teal-fg`, `--coral-fg`): the hue mixed 65% with the ink colour, for text on its own
  14–16% tint. The raw hue is for fills, bars and borders only.

### Record hues
`--hue-blue`, `--hue-teal`, `--hue-violet`, `--hue-purple`, `--hue-amber`, `--hue-coral`, `--hue-red`, `--hue-orange`,
`--hue-slate`, `--hue-slate-dark`, `--hue-grey`, `--hue-cyan`, `--hue-lavender`: one per kind of record (Activities violet,
Prospects amber, Projects coral, Files purple, Machines teal, Costing sheets and Request forms slate…). Pages pass them by
name (`company.js`, `files.js`, `common.js`, `AppPage.cs`), never as hex. They fill the 30px section tiles and the More
sheet's tiles (white icon on top), and colour tags, which mix them with the ink for text and with transparent for the
background (`.tag`), so a tag reads in both themes.

### Neutral
- **Page** (`--bg`, #f2f2f7; dark #000): the page behind everything.
- **Card** (`--card`, #fff; dark #1c1c1e): every block of content. `--surface` / `--surface-2` for hover and wells.
- **Ink** (`--ink`, #111114; dark #f5f5f7): text. `--ink-2` (#454a54) secondary text and labels, `--muted` (#5a5f6b)
  quieter text, `--placeholder` (#676c77; dark #8e8e96) placeholders only.
- **Lines** (`--line`, `--border`, `--border-soft`): hairlines in the ink colour at 9–18% opacity.

### The tag palette (MyCRMRecords' first look)
The first `:root` block of `app.css` is the original MyAllocation palette. Its main colours were replaced by the ones
above; what is left is still used for status and tag colours (`--yellow-*`, `--rose-*`, `--indigo-*`, `--purple-*`,
`--green-*`, `--sky-*`…) and messages (`--ok-*`, `--error-*`). Keep them; don't add to them. A new colour goes in the main
palette.

### Named Rules
**The Readable Hue Rule.** Coloured text is never the raw hue on a tint of itself: use `--accent-ink`, `--teal-fg`,
`--coral-fg`, or mix the hue with `--ink`. Every text colour is at least 4.5:1 in both themes, placeholders included.

**The One Voice Rule.** Blue means "you can act here" or "this is selected". A record hue never marks a button.

## Typography

**Font:** Inter (self-hosted in `fonts/`, OFL), falling back to the device's own font. It replaced the device font
because Segoe UI read poorly on Windows. `cv11` and `ss03` for the single-storey a and straight-sided letters; figures
line up in columns in tables.

**Character:** one neutral sans in a few weights. Hierarchy comes from size and weight steps, not from a second face.

### Hierarchy
- **Page title** (800, 2rem, tight -0.03em): the page's `h1`, a company's name. 1.75rem on a phone.
- **Card title** (700, 1.0625rem): a card's `h2`.
- **Body** (400, 0.9375rem, 1.45): everything else. Tables 0.875rem.
- **Label** (600, 0.8125rem, `--ink-2`): field labels, column heads (sentence case, not capitals), chips.
- **Tab label** (600, 0.6875rem): the phone's bottom bar, under a 24px icon. Never smaller.

### Named Rules
**The Rem Rule.** Every size is in `rem` and every breakpoint in `em`, so someone who sets a larger text size in their
browser gets larger text and, past a point, the phone layout. At the default 16px nothing changes. No `px` font sizes.

## Layout

Content sits in a centred column with 40px side margins on a computer (16px on a phone). Cards stack with 12–20px between
them, with 22px × 24px inside. Lists are tables on a computer and a card per row on a phone (`.file-card`). A company's page is a
two-column overview (details and addresses on the left, stat tiles, salesperson and notes on the right) with its sections as
tabs; on a phone, the details then a **Sections** list, each opening full screen with a way back.

Breakpoints (in `em`, see the Rem Rule): **56.25em** (900px) switches between computer and phone; **35em** (560px) is
the narrow phone; between 56.3125em and 100em the top bar progressively drops labels and gaps so the menu never runs into
the buttons on its right.

## Elevation & Depth

Two layers and nothing in between. **Content is flat**: cards have no shadow and no border, only the white against the
grey page. **Controls float**: glass with `backdrop-filter: blur(24px) saturate(180%)` over `rgba(var(--glass-rgb), .36)`,
a darker rim (`--rim`), a bright inner top edge (`--spec`), and one soft shadow
(`--glass-shadow: 0 10px 30px rgba(0,0,0,.14), 0 1px 3px rgba(0,0,0,.10)`). Without `backdrop-filter`, glass turns 94%
opaque. Inside a card, a control loses its glass and shadow (it is already on a surface).

### Named Rules
**The Glass Ledger Rule.** Glass is for controls only: bars, buttons, capsules, search, menus, dialogs, toasts. Never a
card, a table, a form or a chart.

## Shapes

Everything is round. Capsules (buttons, tabs, filters, search: radius = half the height, 22–24px), cards 24px, chips
and tags 12px at 24px tall, section and More tiles 9px at 30px, the phone's bottom bar a 27px pill. No square corners,
no coloured side borders.

## Components

- **Buttons**: primary is a solid `--accent-fill` capsule with white 700 text (46px at the top of a page, 36px in a
  card); secondary is a glass capsule, grouped in one capsule with dividers when they sit together (*Print summary* |
  *Edit company*); soft is accent text on a 12% accent tint. Press springs to 1.07 (`cubic-bezier(.34,1.56,.64,1)`), hover to
  1.03; both off with reduced motion.
- **Tabs and toggles**: a glass capsule of options; the selected one is a solid `--accent-fill` pill with white text.
  On a phone the options share the width by label length.
- **Filters**: glass capsules with the label inside ("Customer type **All**"); search is a capsule `<label>` around its input,
  so a tap anywhere focuses it.
- **Focus**: a 2px `--focus` outline offset by 1–2px on every control, including capsules that wrap an input
  (`:focus-within`).
- **Chips**: 24px, radius 12px, 600 weight: salesperson (accent), no salesperson (coral), team (teal), always the
  readable text colour on a 14–16% tint of the hue.
- **Section and More tiles**: a 30px rounded square in a record hue with a white 18px icon from `common.js`'s set. Never a
  letter.
- **Stat tiles** (a company's overview): label in the record hue mixed with ink, a large tabular number, one line of
  context; the whole tile opens the tab.
- **Phone bottom bar**: five items (Companies, Files, Machines, Orders, More), icon over an 11px label, items sized by
  their label; More opens the rest of the menu as a sheet.

## Do's and Don'ts

- **Do** name colours by token; a page passes a record hue as `var(--hue-…)`, never a hex.
- **Do** check new text at 4.5:1 in light *and* dark, on the surface it actually sits on (glass lightens what is under it).
- **Do** size text in `rem` and breakpoints in `em`.
- **Don't** put glass on content, or a shadow on a card.
- **Don't** make a fill see-through when it carries white text (`color-mix(... transparent)` behind white text fails
  contrast).
- **Don't** use a raw hue for text on its own tint; use the `-fg` / `-ink` token.
- **Don't** add a second accent, a second typeface, or a coloured side border.
