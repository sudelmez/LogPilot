import { Box, Skeleton, Typography } from '@mui/material';

interface Props {
  title: string;
  value: string;
  caption?: string;
  tone?: 'default' | 'error' | 'warning' | 'success';
  loading?: boolean;
}

/** Durum şeridinin bir hücresi: etiket, değer, kısa açıklama. Renk yalnızca değer bir durumu anlatıyorsa. */
export default function StatCard({ title, value, caption, tone = 'default', loading }: Props) {
  const color = tone === 'default' ? 'text.primary' : `${tone}.main`;
  return (
    <Box sx={{ px: 2, py: 1.5, minWidth: 0 }}>
      <Typography variant="caption" color="text.secondary" component="div" noWrap>{title}</Typography>
      {loading ? (
        <Skeleton width={72} height={34} />
      ) : (
        <Typography sx={{ fontSize: '1.625rem', fontWeight: 650, letterSpacing: '-0.02em', lineHeight: 1.25, color }}>{value}</Typography>
      )}
      {caption && <Typography variant="caption" color="text.secondary" component="div" noWrap title={caption}>{caption}</Typography>}
    </Box>
  );
}
