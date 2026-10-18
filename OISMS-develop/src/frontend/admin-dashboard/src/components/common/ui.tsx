import type { ButtonHTMLAttributes, InputHTMLAttributes, ReactNode, SelectHTMLAttributes } from 'react';
import { X } from 'lucide-react';

export function Button({
  className = '',
  variant = 'primary',
  children,
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: 'primary' | 'secondary' | 'ghost' | 'danger';
}) {
  return (
    <button className={`button button--${variant} ${className}`} {...props}>
      {children}
    </button>
  );
}

export function Input({ className = '', ...props }: InputHTMLAttributes<HTMLInputElement>) {
  return <input className={`input ${className}`} {...props} />;
}

export function Select({ children, className = '', ...props }: SelectHTMLAttributes<HTMLSelectElement>) {
  return (
    <select className={`input ${className}`} {...props}>
      {children}
    </select>
  );
}

export function Badge({
  children,
  tone = 'neutral',
  className = '',
}: {
  children: ReactNode;
  tone?: 'success' | 'warning' | 'danger' | 'info' | 'neutral';
  className?: string;
}) {
  return <span className={`badge badge--${tone} ${className}`}>{children}</span>;
}

export function PageTitle({
  eyebrow,
  title,
  description,
  actions,
}: {
  eyebrow?: string;
  title: string;
  description: string;
  actions?: ReactNode;
}) {
  return (
    <div className="page-title">
      <div>
        {eyebrow && <div className="eyebrow">{eyebrow}</div>}
        <h1>{title}</h1>
        <p>{description}</p>
      </div>
      {actions && <div className="page-actions">{actions}</div>}
    </div>
  );
}

export function StatCard({
  label,
  value,
  change,
  icon,
}: {
  label: string;
  value: string | number;
  change?: string;
  icon?: ReactNode;
}) {
  return (
    <div className="stat-card">
      <div className="stat-head">
        <span>{label}</span>
        {icon && <span className="stat-icon">{icon}</span>}
      </div>
      <strong>{value}</strong>
      {change && <small>{change}</small>}
    </div>
  );
}

export function Modal({
  title,
  children,
  onClose,
  maxWidth,
  wide = false,
}: {
  title: string;
  children: ReactNode;
  onClose: () => void;
  maxWidth?: string;
  wide?: boolean;
}) {
  const resolvedMaxWidth = maxWidth || (wide ? '880px' : '560px');
  return (
    <div
      className="modal-backdrop"
      role="presentation"
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div className="modal" role="dialog" aria-modal="true" style={{ maxWidth: resolvedMaxWidth }}>
        <div className="modal-head">
          <h2>{title}</h2>
          <Button variant="ghost" aria-label="Đóng" onClick={onClose} type="button">
            <X size={20} />
          </Button>
        </div>
        {children}
      </div>
    </div>
  );
}

export function EmptyState({
  title,
  description,
  action,
}: {
  title: string;
  description: string;
  action?: ReactNode;
}) {
  return (
    <div className="empty-state">
      <div className="empty-mark">O</div>
      <strong>{title}</strong>
      <p>{description}</p>
      {action && <div style={{ marginTop: 14 }}>{action}</div>}
    </div>
  );
}
