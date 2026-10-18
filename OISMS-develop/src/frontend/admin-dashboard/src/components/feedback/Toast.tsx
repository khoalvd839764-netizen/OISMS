import { createContext, useCallback, useContext, useState, type ReactNode } from 'react';
import { CheckCircle2, AlertCircle, Info, X } from 'lucide-react';

export type ToastTone = 'success' | 'error' | 'warning' | 'info';

export interface ToastMessage {
  id: string;
  tone: ToastTone;
  title: string;
  message?: string;
}

interface ToastContextValue {
  showToast: (
    toneOrToast: ToastTone | Omit<ToastMessage, 'id'>,
    title?: string,
    message?: string
  ) => void;
  removeToast: (id: string) => void;
}

const ToastContext = createContext<ToastContextValue | null>(null);

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<ToastMessage[]>([]);

  const removeToast = useCallback((id: string) => {
    setToasts((prev) => prev.filter((t) => t.id !== id));
  }, []);

  const showToast = useCallback(
    (
      toneOrToast: ToastTone | Omit<ToastMessage, 'id'>,
      title?: string,
      message?: string
    ) => {
      const id = Math.random().toString(36).substring(2, 9);
      const payload: Omit<ToastMessage, 'id'> =
        typeof toneOrToast === 'string'
          ? { tone: toneOrToast, title: title || '', message }
          : toneOrToast;

      setToasts((prev) => [...prev, { ...payload, id }]);
      setTimeout(() => {
        removeToast(id);
      }, 4500);
    },
    [removeToast]
  );

  return (
    <ToastContext.Provider value={{ showToast, removeToast }}>
      {children}
      <div
        style={{
          position: 'fixed',
          bottom: 24,
          right: 24,
          zIndex: 10000,
          display: 'flex',
          flexDirection: 'column',
          gap: 10,
          maxWidth: 380,
        }}
      >
        {toasts.map((toast) => {
          const isSuccess = toast.tone === 'success';
          const isError = toast.tone === 'error';
          const isWarning = toast.tone === 'warning';
          const bg = isSuccess ? '#e8f7f0' : isError ? '#fff0f1' : isWarning ? '#fff5dc' : '#edf3ff';
          const borderColor = isSuccess ? '#16845b' : isError ? '#c93c48' : isWarning ? '#b76e00' : '#2768d8';
          const iconColor = borderColor;

          return (
            <div
              key={toast.id}
              style={{
                backgroundColor: bg,
                borderLeft: `4px solid ${borderColor}`,
                borderRadius: 8,
                boxShadow: '0 4px 12px rgba(0,0,0,0.1)',
                padding: '12px 14px',
                display: 'flex',
                alignItems: 'flex-start',
                gap: 10,
                animation: 'slide-in-right 0.25s ease-out',
              }}
            >
              <span style={{ color: iconColor, marginTop: 2 }}>
                {isSuccess && <CheckCircle2 size={18} />}
                {isError && <AlertCircle size={18} />}
                {isWarning && <AlertCircle size={18} />}
                {!isSuccess && !isError && !isWarning && <Info size={18} />}
              </span>
              <div style={{ flex: 1 }}>
                <strong style={{ display: 'block', fontSize: 13, color: '#172033' }}>{toast.title}</strong>
                {toast.message && (
                  <p style={{ margin: '3px 0 0', fontSize: 12, color: '#667085', lineHeight: 1.4 }}>
                    {toast.message}
                  </p>
                )}
              </div>
              <button
                type="button"
                onClick={() => removeToast(toast.id)}
                style={{
                  background: 'transparent',
                  border: 'none',
                  color: '#98a2b3',
                  cursor: 'pointer',
                  padding: 2,
                }}
              >
                <X size={16} />
              </button>
            </div>
          );
        })}
      </div>
    </ToastContext.Provider>
  );
}

export function useToast() {
  const context = useContext(ToastContext);
  if (!context) {
    throw new Error('useToast must be used within ToastProvider');
  }
  return context;
}
