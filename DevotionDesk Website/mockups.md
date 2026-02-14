# DevotionDesk Study Suite Mockups

## 1. Desktop Layout (1440px)
- **Overall Grid**: 12 columns, 24px gutters, main content width 1240px, centered with 100px side padding.
- **Background**: Charcoal gradient (#0F0F12 → #1B1B22) with subtle brass shimmer overlay (opacity 12%).
- **Header**: 72px tall, translucent panel with wordmark “DEVOTIONDESK”, session clock, buttons (`Focus Mode`, `Sanctum Timer`). Buttons use gold gradient and pill shape.
- **Panels**:
  - **Left (PDF / EGW)**: spans 6 columns. Contains brass-bordered card housing WebView2 frame (mock screenshot of EGW PDF). Toolbar at top with document dropdown, zoom buttons, page indicator, `Jump to Page` field. Background features faint parchment texture.
  - **Middle (Bible JSON)**: spans 3 columns. Top controls include Book dropdown, Chapter numeric selector, search pill. Verse list uses cards with glowing highlight on active verse; each verse row shows reference, text, and quick actions (`copy`, `link`).
  - **Right (Notes)**: spans 3 columns. Rich text area with textured background, header showing linked verse + PDF page chips, Save/Export buttons, autosave status (“Last saved 2 min ago”), timeline of recent notes at bottom.
- **Interactions**: vertical `GridSplitter` between panels, focus-mode icon near each panel header, micro-animations indicated with arrows.

## 2. Tablet / Narrow Layout (960px)
- Panels stack: PDF on top, Bible in middle, Notes bottom.
- Tab bar pinned at top for quick jumps between sections; each tab shows status icons (e.g., notes count).
- Controls condensed: Book + Chapter combined into segmented control; toolbar icons reorganized into dropdown.
- Notes area uses collapsible drawer for timeline to save vertical space.

## 3. Component Close-ups
- **Toolbar**: highlight button states (default, hover, active) with gold glow and subtle drop shadow; annotate padding (12px vertical, 18px horizontal).
- **Verse Card**: specify fonts (Playfair Display 20px for reference, Source Sans 3 16px for text), highlight color (#F4D4A8) with 20% blur to create glow.
- **Notes Toast**: floating pill “Saved to Devotion Ledger” with fade/slide animation spec (300ms ease-out).
- **Context Chips**: show verse chip and PDF page chip styles with brass outline and inner shadow.

## Visual Tokens
- **Color Palette**: #0F0F12 (base), #1B1B22 (panel), #D0A85C (gold), #F4D4A8 (gold-light), #F3EFE7 (ivory text), #7F8794 (slate accent).
- **Typography**: Playfair Display (Headings), Source Sans 3 (Body/UI). Letterspacing for uppercase pills: 0.2em.
- **Effects**: 30px border radius on cards, 0 30px 80px rgba(0,0,0,0.45) shadows, brass gradient borders (#C79A4B → #F5D2A0).

## Next Mockup Steps
1. Build Figma file with shared styles and components (toolbar, verse list, notes card).
2. Export reference PNGs for developer handoff once finalized.
3. Gather feedback on layout balance before implementing in WPF.
