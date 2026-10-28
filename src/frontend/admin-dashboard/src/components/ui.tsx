import type { ButtonHTMLAttributes, InputHTMLAttributes, ReactNode } from "react";
import { X } from "lucide-react";

export function ComingSoonBadge() {
  return <span className="coming-soon-badge">Coming soon</span>;
}

export function ComingSoonPanel({ title, description }: { title: string; description: string }) {
  return <div className="coming-soon-panel"><ComingSoonBadge /><strong>{title}</strong><p>{description}</p></div>;
}

export function Button({ className = "", variant = "primary", ...props }: ButtonHTMLAttributes<HTMLButtonElement> & { variant?: "primary" | "secondary" | "ghost" | "danger" }) {
  return <button className={`button button--${variant} ${className}`} {...props} />;
}

export function Input(props: InputHTMLAttributes<HTMLInputElement>) {
  return <input className="input" {...props} />;
}

export function Select({ children, ...props }: React.SelectHTMLAttributes<HTMLSelectElement>) {
  return <select className="input" {...props}>{children}</select>;
}

export function Badge({ children, tone = "neutral" }: { children: ReactNode; tone?: "success" | "warning" | "danger" | "info" | "neutral" }) {
  return <span className={`badge badge--${tone}`}>{children}</span>;
}

export function PageTitle({ eyebrow, title, description, actions }: { eyebrow?: string; title: string; description: string; actions?: ReactNode }) {
  return (
    <div className="page-title">
      <div><div className="eyebrow">{eyebrow}</div><h1>{title}</h1><p>{description}</p></div>
      {actions && <div className="page-actions">{actions}</div>}
    </div>
  );
}

export function StatCard({ label, value, change, icon }: { label: string; value: string; change: string; icon: ReactNode }) {
  return <div className="stat-card"><div className="stat-head"><span>{label}</span><span className="stat-icon">{icon}</span></div><strong>{value}</strong><small>{change}</small></div>;
}

export function Modal({ title, children, onClose }: { title: string; children: ReactNode; onClose: () => void }) {
  return <div className="modal-backdrop" role="presentation"><div className="modal" role="dialog" aria-modal="true"><div className="modal-head"><h2>{title}</h2><Button variant="ghost" aria-label="Đóng" onClick={onClose}><X size={20} /></Button></div>{children}</div></div>;
}

export function EmptyState({ title, description }: { title: string; description: string }) {
  return <div className="empty-state"><div className="empty-mark">O</div><strong>{title}</strong><p>{description}</p></div>;
}
