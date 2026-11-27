import type { ButtonHTMLAttributes, CSSProperties, InputHTMLAttributes, ReactNode } from "react";
import { X } from "lucide-react";
import SpotlightCard from "./effects/SpotlightCard";
import { CountUp } from "./effects/CountUp";

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

export function StatCard({ label, value, change, icon, countTo }: {
  label: string;
  value: string;
  change: string;
  icon: ReactNode;
  /** Khi có số liệu thật: đếm từ giá trị cũ tới `value`, hiển thị bằng `format`. */
  countTo?: { value: number; format: (n: number) => string };
}) {
  return (
    <SpotlightCard
      className="stat-card"
      theme="light"
      spotlightColor="#1769e0"
      intensity={0.5}
      spotlightSize={220}
      borderGlow={0.9}
      // Giữ kích thước/màu của .stat-card: class p-8, rounded-3xl của component thắng CSS trong @layer components.
      style={{
        padding: "17px 18px",
        borderRadius: "var(--radius)",
        border: 0,
        "--spotlight-card-surface": "var(--surface)",
        "--spotlight-card-border": "var(--line)",
        "--spotlight-card-shadow": "var(--shadow)",
      } as CSSProperties}
    >
      <div className="stat-head"><span>{label}</span><span className="stat-icon">{icon}</span></div>
      <strong>{countTo ? <CountUp value={countTo.value} format={countTo.format} /> : value}</strong>
      <small>{change}</small>
    </SpotlightCard>
  );
}

export function Modal({ title, children, onClose, wide = false }: { title: string; children: ReactNode; onClose: () => void; wide?: boolean }) {
  return <div className="modal-backdrop" role="presentation"><div className={wide ? "modal modal--wide" : "modal"} role="dialog" aria-modal="true"><div className="modal-head"><h2>{title}</h2><Button variant="ghost" aria-label="Đóng" onClick={onClose}><X size={20} /></Button></div>{children}</div></div>;
}

export function EmptyState({ title, description }: { title: string; description: string }) {
  return <div className="empty-state"><div className="empty-mark">O</div><strong>{title}</strong><p>{description}</p></div>;
}
