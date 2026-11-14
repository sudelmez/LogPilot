import type { ReactNode } from 'react';
import { Box, Typography } from '@mui/material';

/** Boş durum: ne olduğunu ve bundan sonra ne yapılabileceğini söyler; süs ikonu yok. */
export default function EmptyState({ text = 'No data for this period.', action }: { text?: string; action?: ReactNode }) {
  return (
    <Box sx={{ py: 4, px: 2, textAlign: 'center' }}>
      <Typography variant="body2" color="text.secondary" sx={{ maxWidth: '56ch', mx: 'auto' }}>{text}</Typography>
      {action && <Box sx={{ mt: 1.5 }}>{action}</Box>}
    </Box>
  );
}
