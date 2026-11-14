import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { Box, Button, Card, Stack, Tooltip, Typography } from '@mui/material';
import type { SxProps, Theme } from '@mui/material';
import ForumOutlined from '@mui/icons-material/ForumOutlined';
import { MONO } from '../theme';

/** Ürün işareti: üç duraklı bir hat — asistanın tablolar arasında izlediği yolun simgesi. */
export function BrandMark({ size = 22 }: { size?: number }) {
  return (
    <Box component="svg" viewBox="0 0 32 32" sx={{ width: size, height: size, flexShrink: 0, color: 'primary.main' }} aria-hidden>
      <path d="M6 23 L16 10 L26 18" fill="none" stroke="currentColor" strokeWidth={2.5} strokeLinecap="round" strokeLinejoin="round" />
      <circle cx="6" cy="23" r="3.2" fill="currentColor" />
      <Box component="circle" cx="16" cy="10" r="3.2" strokeWidth={2.5} sx={{ fill: 'var(--mui-palette-background-rail)', stroke: 'currentColor' }} />
      <circle cx="26" cy="18" r="3.2" fill="currentColor" />
    </Box>
  );
}

/** Kartın içinde başlık satırı + içerik. Başlık ve açıklama aynı satırda: dikey yer harcamaz. */
export function Panel({ title, caption, actions, children, sx, padding = 2 }: {
  title?: string; caption?: string; actions?: ReactNode; children: ReactNode; sx?: SxProps<Theme>; padding?: number;
}) {
  return (
    <Card sx={sx}>
      {(title || actions) && (
        <Stack direction="row" spacing={1.5} useFlexGap
          sx={{ px: 2, pt: 1.75, pb: 1, alignItems: 'baseline', flexWrap: 'wrap', justifyContent: 'space-between' }}>
          <Stack direction="row" spacing={1.25} useFlexGap sx={{ alignItems: 'baseline', flexWrap: 'wrap', minWidth: 0 }}>
            {title && <Typography variant="subtitle1" component="h2">{title}</Typography>}
            {caption && <Typography variant="caption" color="text.secondary">{caption}</Typography>}
          </Stack>
          {actions}
        </Stack>
      )}
      <Box sx={{ px: padding, pb: padding, pt: title || actions ? 0.5 : padding }}>{children}</Box>
    </Card>
  );
}

export type Tone = 'error' | 'warning' | 'success' | 'info' | 'neutral';

const toneColor: Record<Tone, string> = {
  error: 'error.main', warning: 'warning.main', success: 'success.main', info: 'info.main', neutral: 'text.disabled',
};

/** Durum: renkli nokta + metin. Renk yalnızca durumu taşır; metin her zaman okunur kalır. */
export function StatusDot({ tone, label, strong }: { tone: Tone; label: string; strong?: boolean }) {
  return (
    <Stack component="span" direction="row" spacing={0.75} sx={{ alignItems: 'center', display: 'inline-flex', whiteSpace: 'nowrap' }}>
      <Box component="span" sx={{
        width: 7, height: 7, borderRadius: '50%', bgcolor: toneColor[tone], flexShrink: 0,
        boxShadow: strong && tone !== 'neutral' ? (t: Theme) => `0 0 0 3px color-mix(in srgb, ${t.vars!.palette[tone].main} 22%, transparent)` : undefined,
      }} />
      <Typography component="span" variant="body2" sx={{ fontWeight: strong ? 650 : 500, color: strong ? toneColor[tone] : 'text.primary' }}>
        {label}
      </Typography>
    </Stack>
  );
}

/** Hata kodu, kimlik, poliçe no gibi veri: sabit genişlikli yazı. Tıklanabilir olabilir. */
export function Code({ children, tone, to, title }: { children: ReactNode; tone?: Tone; to?: string; title?: string }) {
  const sx = {
    fontFamily: MONO, fontSize: '0.75rem', fontWeight: 600, lineHeight: 1.6, px: 0.75, borderRadius: '4px', whiteSpace: 'nowrap',
    display: 'inline-block', maxWidth: '100%', overflow: 'hidden', textOverflow: 'ellipsis', verticalAlign: 'middle',
    color: tone && tone !== 'neutral' ? toneColor[tone] : 'text.primary',
    bgcolor: tone && tone !== 'neutral'
      ? (t: Theme) => `color-mix(in srgb, ${t.vars!.palette[tone].main} 12%, transparent)`
      : 'background.raised',
    textDecoration: 'none',
  } as const;
  return to
    ? <Box component={Link} to={to} title={title} sx={{ ...sx, '&:hover': { textDecoration: 'underline' } }}>{children}</Box>
    : <Box component="span" title={title} sx={sx}>{children}</Box>;
}

/** Hatayı gördüğün yerden asistana sor: soru hazırlanmış olarak asistan ekranı açılır (gönderilmez, düzenlenebilir). */
export function AskButton({ question, label = 'Ask assistant', compact }: { question: string; label?: string; compact?: boolean }) {
  const button = (
    <Button component={Link} to={`/assistant?ask=${encodeURIComponent(question)}`} size="small" variant="text"
      startIcon={<ForumOutlined sx={{ fontSize: '16px !important' }} />}
      onClick={(e) => e.stopPropagation()}
      sx={{ flexShrink: 0, minHeight: 26, px: compact ? 0.75 : 1, color: 'primary.main', whiteSpace: 'nowrap' }}>
      {compact ? 'Ask' : label}
    </Button>
  );
  return <Tooltip title={`Ask: “${question}”`}>{button}</Tooltip>;
}

const clamp = (lines: number) => ({ display: '-webkit-box', WebkitLineClamp: lines, WebkitBoxOrient: 'vertical' as const, overflow: 'hidden' });

export interface RouteStop { source: string; lookedUpBy: string; found: boolean; result?: string | null }

/**
 * İmza öğesi: asistanın bir cevaba ulaşmak için gezdiği log tabloları, bir hat haritası gibi.
 * Duraklar tablolar, aralarındaki etiket bir sonraki tabloya geçilen anahtar. Boş kalan durak içi boş çizilir.
 */
export function RouteLine({ stops }: { stops: RouteStop[] }) {
  if (stops.length === 0) return null;
  return (
    <Box component="ol" aria-label="Investigation path" sx={{
      listStyle: 'none', m: 0, p: 0, display: 'grid', gap: { xs: 0, md: 0 },
      gridTemplateColumns: { xs: '1fr', md: `repeat(${stops.length}, minmax(0, 1fr))` },
    }}>
      {stops.map((s, i) => {
        const last = i === stops.length - 1;
        return (
          <Box component="li" key={`${s.source}-${i}`} sx={{
            position: 'relative', display: 'grid', columnGap: 1.25,
            gridTemplateColumns: { xs: '14px 1fr', md: '1fr' }, gridTemplateRows: { md: '14px auto' },
            pb: { xs: last ? 0 : 1.5, md: 0 }, pr: { md: 1.5 },
          }}>
            {/* hat: masaüstünde yatay, telefonda dikey */}
            {!last && (
              <Box aria-hidden sx={{
                position: 'absolute', bgcolor: s.found ? 'primary.main' : 'divider',
                left: { xs: 6, md: 14 }, top: { xs: 14, md: 6 },
                width: { xs: 2, md: 'calc(100% - 14px)' }, height: { xs: 'calc(100% - 14px)', md: 2 },
              }} />
            )}
            <Box aria-hidden sx={{
              width: 14, height: 14, borderRadius: '50%', position: 'relative', zIndex: 1, boxSizing: 'border-box',
              border: 2.5, borderColor: s.found ? 'primary.main' : 'text.disabled',
              bgcolor: s.found ? 'primary.main' : 'background.paper',
            }} />
            <Box sx={{ minWidth: 0, pt: { md: 0.75 } }}>
              {/* Uzun tablo adları alt çizgiden kırılır; açıklamalar 3 satırla sınırlı, tamamı ipucunda. */}
              <Typography component="div" sx={{ fontFamily: MONO, fontSize: '0.8125rem', fontWeight: 650, lineHeight: 1.35,
                overflowWrap: 'anywhere', color: s.found ? 'text.primary' : 'text.secondary' }}>
                {s.source.replaceAll('_', '_\u200b')}
              </Typography>
              <Typography variant="caption" color="text.secondary" component="div" title={s.lookedUpBy}
                sx={{ fontFamily: MONO, overflowWrap: 'anywhere', ...clamp(2) }}>
                {s.lookedUpBy}
              </Typography>
              {s.result && (
                <Typography variant="caption" component="div" title={s.result}
                  sx={{ mt: 0.25, color: s.found ? 'text.primary' : 'text.secondary', overflowWrap: 'anywhere', ...clamp(3) }}>
                  {s.found ? '' : 'Nothing found · '}{s.result}
                </Typography>
              )}
            </Box>
          </Box>
        );
      })}
    </Box>
  );
}

/** Kart içi bölüm başlığı (ör. "Probable cause"): küçük, ikincil renk; üstünde etiket yok. */
export function SectionLabel({ children }: { children: ReactNode }) {
  return <Typography variant="subtitle2" component="h3" color="text.secondary" sx={{ mb: 0.75 }}>{children}</Typography>;
}
