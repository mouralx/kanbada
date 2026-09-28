export type ThemePalette = ReturnType<typeof themePalette>;
export type ThemeOptions = {
  primary: string;
  accent: string;
  lightBackground: string;
  darkBackground: string;
  lightSurface?: string | null;
  darkSurface?: string | null;
  lightText?: string | null;
  darkText?: string | null;
  lightBorder?: string | null;
  darkBorder?: string | null;
  sidebarBackground?: string | null;
  sidebarText?: string | null;
  fontFamily?: string;
  fontScale?: number;
  cornerRadius?: number;
};

export const isThemeColor = (value: string | null | undefined): value is string =>
  typeof value === 'string' && /^#[\da-f]{6}$/i.test(value);
const rgb = (color: string) =>
  [1, 3, 5].map((start) => parseInt(color.slice(start, start + 2), 16));
const luminance = (color: string) =>
  rgb(color)
    .map((v) => {
      const channel = v / 255;
      return channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4;
    })
    .reduce((sum, v, i) => sum + v * [0.2126, 0.7152, 0.0722][i], 0);
export function contrast(a: string, b: string) {
  const values = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (values[0] + 0.05) / (values[1] + 0.05);
}
export function mix(a: string, b: string, amount: number) {
  const end = rgb(b);
  return (
    '#' +
    rgb(a)
      .map((v, i) =>
        Math.round(v + (end[i] - v) * amount)
          .toString(16)
          .padStart(2, '0'),
      )
      .join('')
  );
}
export function foreground(color: string) {
  return contrast('#000000', color) > contrast('#ffffff', color) ? '#000000' : '#ffffff';
}
function readable(preferred: string | null | undefined, background: string, ratio = 5) {
  const base = isThemeColor(preferred) ? preferred : foreground(background);
  if (contrast(base, background) >= ratio) return base;
  const target = foreground(background);
  for (let step = 1; step <= 100; step++) {
    const candidate = mix(base, target, step / 100);
    if (contrast(candidate, background) >= ratio) return candidate;
  }
  return target;
}
export function themePalette(options: ThemeOptions, mode: 'light' | 'dark') {
  const color = (value: string | null | undefined, fallback: string) =>
    isThemeColor(value) ? value : fallback;
  const background = color(
    mode === 'light' ? options.lightBackground : options.darkBackground,
    mode === 'light' ? '#f8f9fb' : '#17191c',
  );
  const surface = color(
    mode === 'light' ? options.lightSurface : options.darkSurface,
    mode === 'light' && luminance(background) > 0.5
      ? mix(background, '#ffffff', 0.8)
      : mix(background, '#ffffff', 0.055),
  );
  const preferredText = mode === 'light' ? options.lightText : options.darkText;
  const text = readable(preferredText ?? (mode === 'light' ? '#303338' : '#e5e5e7'), surface);
  const primary = color(options.primary, '#334153');
  const accent = color(options.accent, '#aac2e1');
  const sidebar = color(
    options.sidebarBackground,
    primary.toLowerCase() === '#334153' ? '#22262c' : mix(primary, '#080b12', 0.78),
  );
  return {
    bg: background,
    surface,
    field: mix(surface, foreground(surface), 0.035),
    raised: mix(surface, accent, 0.08),
    text,
    pageText: readable(preferredText, background),
    muted: readable(mix(text, surface, 0.3), surface),
    border: color(
      mode === 'light' ? options.lightBorder : options.darkBorder,
      mix(surface, text, 0.22),
    ),
    primary,
    onPrimary: foreground(primary),
    accent,
    onAccent: foreground(accent),
    link: readable(primary, surface),
    selection: mix(surface, accent, 0.18),
    sidebar,
    sidebarText: readable(options.sidebarText ?? '#e6e9ed', sidebar),
    sidebarMuted: readable(mix(sidebar, readable(options.sidebarText, sidebar), 0.7), sidebar),
    sidebarBorder: mix(sidebar, foreground(sidebar), 0.25),
    sidebarPanel: mix(sidebar, foreground(sidebar), 0.055),
  };
}
export function themeVariables(options: ThemeOptions) {
  const result: Record<string, string> = {};
  for (const mode of ['light', 'dark'] as const) {
    const palette = themePalette(options, mode);
    for (const [key, value] of Object.entries(palette))
      result[`--ui-${mode}-${key.replace(/[A-Z]/g, (s) => '-' + s.toLowerCase())}`] = value;
    result[`--ui-${mode}-select-arrow`] = `url("data:image/svg+xml,${encodeURIComponent(
      `<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="${palette.text}" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m6 9 6 6 6-6"/></svg>`,
    )}")`;
  }
  const fonts: Record<string, string> = {
    default: "'DM Sans', sans-serif",
    system: 'system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif',
    'dm-sans': "'DM Sans', sans-serif",
    manrope: "'Manrope', sans-serif",
  };
  result['--ui-font-body'] = fonts[options.fontFamily ?? 'default'] ?? fonts.default;
  result['--ui-font-heading'] =
    options.fontFamily && options.fontFamily !== 'default'
      ? result['--ui-font-body']
      : "'Manrope', sans-serif";
  result['--ui-font-scale'] = String((options.fontScale ?? 100) / 100);
  result['--ui-radius-scale'] = String((options.cornerRadius ?? 8) / 8);
  return result;
}
