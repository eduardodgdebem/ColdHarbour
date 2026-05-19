
---

name: Sonic Brutalism
colors:
  surface: '#f9f9f9'
  surface-dim: '#dadada'
  surface-bright: '#f9f9f9'
  surface-container-lowest: '#ffffff'
  surface-container-low: '#f3f3f3'
  surface-container: '#eeeeee'
  surface-container-high: '#e8e8e8'
  surface-container-highest: '#e2e2e2'
  on-surface: '#1b1b1b'
  on-surface-variant: '#4c4546'
  inverse-surface: '#303030'
  inverse-on-surface: '#f1f1f1'
  outline: '#7e7576'
  outline-variant: '#cfc4c5'
  surface-tint: '#5e5e5e'
  primary: '#000000'
  on-primary: '#ffffff'
  primary-container: '#1b1b1b'
  on-primary-container: '#848484'
  inverse-primary: '#c6c6c6'
  secondary: '#5d5f5b'
  on-secondary: '#ffffff'
  secondary-container: '#e0e0db'
  on-secondary-container: '#62635f'
  tertiary: '#000000'
  on-tertiary: '#ffffff'
  tertiary-container: '#191e00'
  on-tertiary-container: '#7a8c00'
  error: '#ba1a1a'
  on-error: '#ffffff'
  error-container: '#ffdad6'
  on-error-container: '#93000a'
  primary-fixed: '#e2e2e2'
  primary-fixed-dim: '#c6c6c6'
  on-primary-fixed: '#1b1b1b'
  on-primary-fixed-variant: '#474747'
  secondary-fixed: '#e3e3de'
  secondary-fixed-dim: '#c6c7c2'
  on-secondary-fixed: '#1a1c19'
  on-secondary-fixed-variant: '#454744'
  tertiary-fixed: '#d3f000'
  tertiary-fixed-dim: '#b9d300'
  on-tertiary-fixed: '#191e00'
  on-tertiary-fixed-variant: '#414c00'
  background: '#f9f9f9'
  on-background: '#1b1b1b'
  surface-variant: '#e2e2e2'
typography:
  display-xl:
    fontFamily: Archivo Narrow
    fontSize: 80px
    fontWeight: '800'
    lineHeight: '1.0'
    letterSpacing: -0.04em
  headline-lg:
    fontFamily: Archivo Narrow
    fontSize: 48px
    fontWeight: '700'
    lineHeight: '1.1'
    letterSpacing: -0.02em
  headline-lg-mobile:
    fontFamily: Archivo Narrow
    fontSize: 32px
    fontWeight: '700'
    lineHeight: '1.1'
  headline-md:
    fontFamily: Archivo Narrow
    fontSize: 24px
    fontWeight: '700'
    lineHeight: '1.2'
  body-lg:
    fontFamily: Public Sans
    fontSize: 18px
    fontWeight: '600'
    lineHeight: '1.5'
  body-md:
    fontFamily: Public Sans
    fontSize: 16px
    fontWeight: '400'
    lineHeight: '1.5'
  label-sm:
    fontFamily: Space Mono
    fontSize: 12px
    fontWeight: '700'
    lineHeight: '1.0'
spacing:
  border-width: 4px
  stack-gap: 0px
  container-padding: 24px
  grid-gutter: 16px
  margin-sm: 8px
  margin-md: 16px
  margin-lg: 32px
---

## Brand & Style

The design system is rooted in **Neo-Brutalism**, specifically tailored for a high-energy music environment. It rejects the polished, safe aesthetics of modern SaaS in favor of a raw, "print-first" digital expression. The brand personality is unapologetic, functional, and loud.

The UI should evoke the feeling of a physical fanzine or a vintage industrial equipment interface. By using heavy strokes, stark contrasts, and a complete absence of gradients or soft shadows, the design system creates a sense of tactile permanence. It targets an audience that values authenticity and clear information hierarchy over decorative flourishes.

**Visual Principles:**

- **Honesty in Form:** Elements look like what they do; buttons are clear blocks, sliders are thick bars.
- **Structural Rigidity:** Every element is locked into a visible or perceived grid.
- **Aggressive Typography:** Type is not just for reading; it is a primary graphic element.

## Colors

The palette is built on extreme contrast to ensure maximum legibility and visual impact.

- **Primary (Stark Black):** Used for all borders (4px), primary text, and heavy structural containers.
- **Secondary (Off-White/Paper):** The default background color (#F5F5F0). It provides a slightly warmer, more organic feel than pure white, mimicking high-quality paper stock.
- **Tertiary (Acidic Yellow):** The main functional accent color (#E0FF00). Used for highlights, active states, and "Now Playing" indicators.
- **Electric Blue:** Used sparingly for secondary interactive cues or specific category filtering to prevent the yellow from becoming overwhelming.

**Application Rules:**

- Avoid shades of gray. Use black or off-white.
- High-contrast combinations only (Black on Yellow, Black on Off-White, White on Black).

## Typography

This design system uses a triple-threat typographic approach to differentiate between action, information, and data.

- **Headlines (Archivo Narrow):** Set in heavy weights with tight tracking. It should feel massive and "pressed" into the layout. Use uppercase for all level 1 and 2 headers.
- **Body (Public Sans):** A neutral, highly legible sans-serif for tracklists, descriptions, and settings. Use medium/bold weights for readability against high-contrast backgrounds.
- **Labels (Space Mono):** Used for technical data, timestamps (04:20), and metadata. This reinforces the "equipment" aesthetic.

**Formatting:**

- No italics, except in rare editorial contexts.
- Use "Block" alignment for large text segments where possible.

## Layout & Spacing

The layout is a **Fixed Grid** system that emphasizes "contained" areas. Every section of the music player (Sidebar, Player Controls, Library) is treated as a distinct box defined by 4px black borders.

**Layout Model:**

- **The Box Model:** Elements should touch. Instead of using whitespace to separate sections, use the 4px border.
- **12-Column Grid:** For desktop library views, utilizing 16px gutters.
- **Margins:** 24px consistent outer padding for the main application window.
- **Mobile Reflow:** On mobile, the 3-column layout stacks vertically. The "Now Playing" bar becomes a fixed block at the bottom, maintaining its 4px top border to separate it from the scrollable library.

**Spacing Rhythm:** Use increments of 8px. However, the most critical "spacing" is the border itself; elements should feel "packed" into their containers.

## Elevation & Depth

This design system rejects all simulated depth. There are no Z-axis shadows, no blurs, and no lighting effects.

**Depth Hierarchy:**

- **Base Layer:** The Off-White (#F5F5F0) background.
- **Structural Layer:** All containers are outlined with `border-width: 4px` in Stark Black.
- **Active Layer:** Use the Acidic Yellow (#E0FF00) as a fill color to indicate the "raised" or "active" state of a component.
- **Hard Shadows (Optional):** If depth is absolutely required for a floating modal, use a "Hard Shadow"—a solid black offset block (e.g., `4px 4px 0px 0px #000000`) rather than a blurred shadow.

## Shapes

The shape language is strictly **Sharp**.

- **Corner Radius:** 0px across all elements. This includes buttons, input fields, album art containers, and the application window itself.
- **Stroke:** A constant 4px black border must be applied to all interactive elements and primary containers.
- **Dividers:** Use solid 4px lines to separate list items (e.g., tracks in a playlist) rather than 1px subtle lines.

## Components

### Buttons

- **Default:** Off-white background, 4px black border, black Archivo Narrow text (uppercase).
- **Primary/Action:** Acidic Yellow background, 4px black border.
- **Interaction:** On hover or click, the button should invert (Black background, Yellow text) or shift 2px down/right to simulate a physical press.

### Track Lists

- Each row is separated by a 4px black bottom border.
- Hover state: Entire row background changes to Acidic Yellow.
- Active/Playing state: Row has a "Playing" label in Space Mono and an Acidic Yellow background.

### Input Fields (Search)

- Sharp corners, 4px black border.
- Placeholder text in Space Mono.
- On focus, the border remains 4px but the background may shift to a very light blue or stay off-white with a thick blinking cursor.

### Sliders (Volume/Progress)

- **Track:** 8px solid black bar.
- **Handle:** A large 24x24px square (sharp corners) with a 4px black border and Acidic Yellow fill.

### Cards (Album Art)

- Album images must be contained within a 4px black border.
- Metadata (Title/Artist) is placed in a separate block immediately below the image, sharing the same border-width.

### Chips/Tags

- Small rectangular blocks with 2px or 4px borders.
- Backgrounds in Electric Blue or Acidic Yellow to categorize genres.
