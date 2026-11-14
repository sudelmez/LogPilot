import { useColorScheme } from '@mui/material/styles';
import { darkPalette, lightPalette } from '../theme';

/**
 * Recharts SVG nitelikleri (fill, stroke) CSS değişkeni kabul etmez: o an seçili şemanın gerçek renklerini döndürür.
 * Şema değişince bileşen yeniden çizilir ve grafik yeni renkleri alır.
 */
export function useChartColors() {
  const { colorScheme } = useColorScheme();
  const p = colorScheme === 'light' ? lightPalette : darkPalette;
  return {
    error: p.error.main,
    warning: p.warning.main,
    success: p.success.main,
    primary: p.primary.main,
    grid: p.divider,
    text: p.text.secondary,
    textPrimary: p.text.primary,
    raised: p.background.raised,
  };
}
