/** Bir log veya hata için asistana hazır soru. Somut kimlik (kullanıcı, korelasyon) varsa onunla: asistan doğru akışı bulur. */
export function askAboutLog(log: { errorCode?: string | null; level: string; userCode?: string | null; correlationId?: string | null; service: string }) {
  const code = log.errorCode ?? `${log.level} log`;
  if (log.userCode) return `${log.userCode} neden ${code} hatası aldı?`;
  if (log.correlationId) return `${log.correlationId} akışında ${code} neden oluştu?`;
  return `${log.service} servisindeki ${code} hatasının nedeni ne?`;
}

export const askAboutCode = (code: string) => `${code} hatası neden oluyor ve kimleri etkiliyor?`;

export const askAboutIncident = (errorCode: string, services: string[]) =>
  `${errorCode} incident'ının nedeni ne${services.length ? `, ${services.slice(0, 3).join(', ')} servislerinde neler oluyor` : ''}?`;
