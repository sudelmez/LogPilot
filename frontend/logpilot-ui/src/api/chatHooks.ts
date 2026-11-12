import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from './client';
import type { ChatMessage, ChatSession } from './chatTypes';
import { rangeParams } from '../lib/ranges';
import type { RangeKey } from '../lib/ranges';

export const useChatSessions = () =>
  useQuery({ queryKey: ['chat-sessions'], queryFn: () => api<ChatSession[]>('/api/chat/sessions') });

export const useChatMessages = (sessionId: string | null) =>
  useQuery({
    queryKey: ['chat-messages', sessionId],
    queryFn: () => api<ChatMessage[]>(`/api/chat/sessions/${sessionId}/messages`),
    enabled: sessionId !== null,
  });

export const useCreateSession = () => {
  const client = useQueryClient();
  return useMutation({
    mutationFn: () => api<ChatSession>('/api/chat/sessions', { method: 'POST', body: JSON.stringify({}) }),
    onSuccess: () => client.invalidateQueries({ queryKey: ['chat-sessions'] }),
  });
};

export interface SendInput {
  sessionId: string;
  content: string;
  range: RangeKey;
}

export const useSendMessage = () => {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (input: SendInput) =>
      api<ChatMessage>(`/api/chat/sessions/${input.sessionId}/messages`, {
        method: 'POST',
        body: JSON.stringify({ content: input.content, ...rangeParams(input.range) }),
      }),
    onSettled: (_data, _error, input) => {
      void client.invalidateQueries({ queryKey: ['chat-messages', input.sessionId] });
      void client.invalidateQueries({ queryKey: ['chat-sessions'] });
    },
  });
};

export interface FeedbackInput {
  messageId: string;
  sessionId: string;
  helpful: boolean;
  resolution?: string;
  rootCause?: string;
}

/** 👍/👎. 👍 + çözüm notu → doğrulanmış vaka olarak hafızaya girer. */
export const useSubmitFeedback = () => {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (input: FeedbackInput) =>
      api<ChatMessage>(`/api/chat/messages/${input.messageId}/feedback`, {
        method: 'POST',
        body: JSON.stringify({ helpful: input.helpful, resolution: input.resolution, rootCause: input.rootCause }),
      }),
    onSuccess: (_data, input) => {
      void client.invalidateQueries({ queryKey: ['chat-messages', input.sessionId] });
      void client.invalidateQueries({ queryKey: ['knowledge'] });
    },
  });
};
