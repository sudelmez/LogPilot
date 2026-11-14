import { createTheme } from '@mui/material/styles';
import type { Theme } from '@mui/material/styles';

// "Sakin konsol": camgöbeği tonlu arduvaz zemin, tek vurgu rengi (sinyal mavisi), durum renkleri yalnızca durum için.
// Koyu ve açık şema aynı rolleri taşır; varsayılan sistem tercihidir (uzun oturumlar, loş oda: koyu).

declare module '@mui/material/styles' {
  interface TypeBackground {
    rail: string;     // sol menü / kabuk
    raised: string;   // satır vurgusu, kod blokları, ikincil yüzey
  }
  interface TypographyVariants { mono: React.CSSProperties }
  interface TypographyVariantsOptions { mono?: React.CSSProperties }
}
declare module '@mui/material/Typography' {
  interface TypographyPropsVariantOverrides { mono: true }
}

/** Şemaya göre değişen renk değişkenleri (koyu/açık): bileşen biçimlerinde sabit renk yerine bunlar kullanılır. */
const pv = (t: Theme) => t.vars!.palette;
/** CSS değişkeni olan bir rengi saydamlaştırır (alpha() değişkenlerle çalışmaz). */
export const mix = (color: string, percent: number) => `color-mix(in srgb, ${color} ${percent}%, transparent)`;

export const MONO = 'ui-monospace, "SF Mono", SFMono-Regular, Menlo, Consolas, "Liberation Mono", monospace';
const SANS = '-apple-system, BlinkMacSystemFont, "Segoe UI Variable Text", "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif';

export const darkPalette = {
  primary: { main: '#7AB0FF', contrastText: '#06111F' },
  error: { main: '#F2686F', contrastText: '#1A0507' },
  warning: { main: '#E9A63B', contrastText: '#1A1004' },
  success: { main: '#4EC28A', contrastText: '#03140B' },
  info: { main: '#7AB0FF', contrastText: '#06111F' },
  background: { default: '#0E1416', paper: '#131B1E', rail: '#0A1012', raised: '#1A2428' },
  text: { primary: '#E4ECEE', secondary: '#9BAEB3', disabled: '#5E7176' },
  divider: '#243236',
};

export const lightPalette = {
  primary: { main: '#1F5FBF', contrastText: '#FFFFFF' },
  error: { main: '#C2333B', contrastText: '#FFFFFF' },
  warning: { main: '#A86A07', contrastText: '#FFFFFF' },
  success: { main: '#1E7F4F', contrastText: '#FFFFFF' },
  info: { main: '#1F5FBF', contrastText: '#FFFFFF' },
  background: { default: '#F3F6F6', paper: '#FFFFFF', rail: '#E6ECEC', raised: '#F0F4F4' },
  text: { primary: '#0F1A1D', secondary: '#4C5F64', disabled: '#8A9A9E' },
  divider: '#D5DEDF',
};

export const theme = createTheme({
  cssVariables: { colorSchemeSelector: 'data-mui-color-scheme' },
  colorSchemes: { dark: { palette: darkPalette }, light: { palette: lightPalette } },
  shape: { borderRadius: 8 },
  spacing: 8,
  typography: {
    fontFamily: SANS,
    fontSize: 14,
    // Ürün arayüzü: sabit rem ölçeği, ~1.2 oran, tek aile.
    h4: { fontSize: '1.75rem', fontWeight: 650, letterSpacing: '-0.02em', lineHeight: 1.15 },
    h5: { fontSize: '1.375rem', fontWeight: 650, letterSpacing: '-0.015em', lineHeight: 1.2 },
    h6: { fontSize: '1rem', fontWeight: 650, letterSpacing: '-0.005em', lineHeight: 1.3 },
    subtitle1: { fontSize: '0.9375rem', fontWeight: 600 },
    subtitle2: { fontSize: '0.8125rem', fontWeight: 600, lineHeight: 1.4 },
    body1: { fontSize: '0.9375rem', lineHeight: 1.55 },
    body2: { fontSize: '0.8125rem', lineHeight: 1.5 },
    caption: { fontSize: '0.75rem', lineHeight: 1.45 },
    overline: { fontSize: '0.6875rem', fontWeight: 600, letterSpacing: '0.06em', lineHeight: 1.6 },
    button: { textTransform: 'none', fontWeight: 600, letterSpacing: 0 },
    mono: { fontFamily: MONO, fontSize: '0.8125rem', lineHeight: 1.5 },
  },
  components: {
    MuiCssBaseline: {
      styleOverrides: (t) => ({
        html: { WebkitFontSmoothing: 'antialiased', MozOsxFontSmoothing: 'grayscale' },
        body: { fontVariantNumeric: 'tabular-nums' },
        '::selection': { background: mix(pv(t).primary.main, 32) },
        '*': { scrollbarWidth: 'thin', scrollbarColor: `${pv(t).divider} transparent` },
        ':focus-visible': { outline: `2px solid ${pv(t).primary.main}`, outlineOffset: 2 },
        a: { color: pv(t).primary.main, textUnderlineOffset: '0.2em' },
        'code, pre, kbd': { fontFamily: MONO },
      }),
    },
    MuiPaper: {
      defaultProps: { elevation: 0 },
      styleOverrides: { root: { backgroundImage: 'none' } },
    },
    MuiCard: {
      defaultProps: { variant: 'outlined' },
      styleOverrides: { root: ({ theme: t }) => ({ borderColor: pv(t).divider, borderRadius: 8 }) },
    },
    MuiCardContent: {
      styleOverrides: { root: { padding: 16, '&:last-child': { paddingBottom: 16 } } },
    },
    MuiButton: {
      defaultProps: { disableElevation: true },
      styleOverrides: {
        root: { borderRadius: 6, paddingInline: 12, minHeight: 32, transition: 'background-color 150ms, border-color 150ms, color 150ms' },
        sizeSmall: { minHeight: 28, paddingInline: 10, fontSize: '0.8125rem' },
        outlined: ({ theme: t }) => ({ borderColor: pv(t).divider }),
      },
    },
    MuiIconButton: { styleOverrides: { root: { borderRadius: 6 } } },
    MuiChip: {
      styleOverrides: {
        root: { borderRadius: 5, fontWeight: 600, height: 22 },
        label: { paddingInline: 7, fontSize: '0.75rem' },
        outlined: ({ theme: t }) => ({ borderColor: pv(t).divider }),
      },
    },
    MuiToggleButtonGroup: {
      styleOverrides: { root: ({ theme: t }) => ({ backgroundColor: pv(t).background.paper }) },
    },
    MuiToggleButton: {
      styleOverrides: {
        root: ({ theme: t }) => ({
          borderColor: pv(t).divider, color: pv(t).text.secondary, paddingBlock: 4, fontSize: '0.8125rem', fontWeight: 600,
          '&.Mui-selected': { color: pv(t).primary.main, backgroundColor: mix(pv(t).primary.main, 12) },
        }),
      },
    },
    MuiOutlinedInput: {
      styleOverrides: {
        root: ({ theme: t }) => ({
          backgroundColor: pv(t).background.paper,
          '& .MuiOutlinedInput-notchedOutline': { borderColor: pv(t).divider },
          '&:hover .MuiOutlinedInput-notchedOutline': { borderColor: pv(t).text.disabled },
        }),
      },
    },
    MuiTableCell: {
      styleOverrides: {
        root: ({ theme: t }) => ({ borderColor: pv(t).divider, paddingBlock: 7 }),
        head: ({ theme: t }) => ({
          fontSize: '0.75rem', fontWeight: 600, color: pv(t).text.secondary, whiteSpace: 'nowrap',
          backgroundColor: pv(t).background.paper,
        }),
      },
    },
    MuiTableRow: {
      styleOverrides: {
        root: ({ theme: t }) => ({
          '&.MuiTableRow-hover:hover': { backgroundColor: pv(t).background.raised },
          '&.Mui-selected, &.Mui-selected:hover': { backgroundColor: mix(pv(t).primary.main, 10) },
        }),
      },
    },
    MuiDrawer: {
      styleOverrides: { paper: ({ theme: t }) => ({ borderColor: pv(t).divider, backgroundImage: 'none' }) },
    },
    MuiTooltip: {
      styleOverrides: {
        tooltip: ({ theme: t }) => ({
          backgroundColor: pv(t).background.raised, color: pv(t).text.primary, border: `1px solid ${pv(t).divider}`,
          fontSize: '0.75rem', boxShadow: '0 6px 20px rgba(0,0,0,0.25)',
        }),
        arrow: ({ theme: t }) => ({ color: pv(t).background.raised }),
      },
    },
    MuiAlert: {
      styleOverrides: { root: { borderRadius: 8, alignItems: 'flex-start' } },
    },
    MuiLinearProgress: {
      styleOverrides: { root: { borderRadius: 3, height: 4 } },
    },
    MuiListItemButton: {
      styleOverrides: {
        root: ({ theme: t }) => ({
          borderRadius: 6,
          '&.Mui-selected, &.Mui-selected:hover': { backgroundColor: mix(pv(t).primary.main, 14), color: pv(t).text.primary },
        }),
      },
    },
    MuiSkeleton: { defaultProps: { animation: 'wave' } },
  },
});
