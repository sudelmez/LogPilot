import { Box, Typography } from '@mui/material';
import InboxOutlined from '@mui/icons-material/InboxOutlined';

export default function EmptyState({ text = 'No data for this period.' }: { text?: string }) {
  return (
    <Box sx={{ py: 5, textAlign: 'center', color: 'text.secondary' }}>
      <InboxOutlined sx={{ fontSize: 36, opacity: 0.6 }} />
      <Typography variant="body2">{text}</Typography>
    </Box>
  );
}
