import type { CSSProperties } from 'react';

export function Skeleton({
  width = '100%',
  height = '20px',
  borderRadius = '6px',
  className = '',
  style,
}: {
  width?: string | number;
  height?: string | number;
  borderRadius?: string | number;
  className?: string;
  style?: CSSProperties;
}) {
  return (
    <div
      className={`skeleton ${className}`}
      style={{
        width,
        height,
        borderRadius,
        backgroundColor: '#e6eaf0',
        animation: 'skeleton-pulse 1.5s ease-in-out infinite',
        ...style,
      }}
    />
  );
}

export function TableRowSkeleton({ columns = 5 }: { columns?: number }) {
  return (
    <tr>
      {Array.from({ length: columns }).map((_, index) => (
        <td key={index} style={{ padding: '14px 16px' }}>
          <Skeleton height={18} width={index === 0 ? '70%' : '50%'} />
        </td>
      ))}
    </tr>
  );
}

export function TableSkeleton({
  rows = 5,
  columns = 5,
}: {
  rows?: number;
  columns?: number;
}) {
  return (
    <div className="table-wrap">
      <table>
        <tbody>
          {Array.from({ length: rows }).map((_, idx) => (
            <TableRowSkeleton key={idx} columns={columns} />
          ))}
        </tbody>
      </table>
    </div>
  );
}

export function CardSkeleton() {
  return (
    <div className="card" style={{ padding: 20 }}>
      <Skeleton height={24} width="40%" style={{ marginBottom: 12 }} />
      <Skeleton height={16} width="80%" style={{ marginBottom: 8 }} />
      <Skeleton height={16} width="60%" />
    </div>
  );
}
