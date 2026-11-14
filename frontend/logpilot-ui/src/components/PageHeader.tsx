import type { ReactNode } from 'react';
import { Stack, Typography } from '@mui/material';

/** Sayfanın tek başlığı (h1), kısa açıklaması ve sağda sayfa düzeyi eylemler (ör. zaman aralığı). */
export default function PageHeader({ title, subtitle, actions }: { title: string; subtitle?: string; actions?: ReactNode }) {
  return (
    <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5}
      sx={{ justifyContent: 'space-between', alignItems: { sm: 'flex-end' }, mb: 2.5 }}>
      <div>
        <Typography variant="h5" component="h1">{title}</Typography>
        {subtitle && <Typography variant="body2" color="text.secondary" sx={{ mt: 0.25, maxWidth: '72ch' }}>{subtitle}</Typography>}
      </div>
      {actions}
    </Stack>
  );
}
