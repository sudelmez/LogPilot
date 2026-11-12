import { Card, CardContent, Typography } from '@mui/material';
import PageHeader from './PageHeader';

export default function ComingSoon({ title, text }: { title: string; text: string }) {
  return (
    <>
      <PageHeader title={title} />
      <Card>
        <CardContent sx={{ py: 6, textAlign: 'center' }}>
          <Typography variant="h6" gutterBottom>Coming soon</Typography>
          <Typography color="text.secondary">{text}</Typography>
        </CardContent>
      </Card>
    </>
  );
}
