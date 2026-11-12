import type { ReactNode } from 'react';
import type { UseQueryResult } from '@tanstack/react-query';
import { Alert, Button, Skeleton } from '@mui/material';
import EmptyState from './EmptyState';

interface Props<T> {
  query: UseQueryResult<T>;
  children: (data: T) => ReactNode;
  isEmpty?: (data: T) => boolean;
  emptyText?: string;
  height?: number;
}

/** Yükleniyor / hata / boş / veri durumlarını tek yerde yönetir: her sayfa aynı davranır. */
export default function QueryState<T>({ query, children, isEmpty, emptyText, height = 180 }: Props<T>) {
  if (query.isPending) return <Skeleton variant="rounded" height={height} />;

  if (query.isError) {
    return (
      <Alert
        severity="error"
        action={<Button color="inherit" size="small" onClick={() => void query.refetch()}>Retry</Button>}
      >
        {query.error instanceof Error ? query.error.message : 'Something went wrong.'}
      </Alert>
    );
  }

  const data = query.data as T;
  if (isEmpty?.(data)) return <EmptyState text={emptyText} />;
  return <>{children(data)}</>;
}
