import { Component, type ErrorInfo, type ReactNode } from 'react';
import { AlertTriangle, RefreshCw } from 'lucide-react';

interface ErrorBoundaryProps {
  children: ReactNode;
}

interface ErrorBoundaryState {
  hasError: boolean;
  errorMessage: string | null;
}

export class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  constructor(props: ErrorBoundaryProps) {
    super(props);
    this.state = {
      hasError: false,
      errorMessage: null,
    };
  }

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return {
      hasError: true,
      errorMessage: error.message || 'Đã xảy ra lỗi không mong muốn trên giao diện.',
    };
  }

  componentDidCatch(error: Error, errorInfo: ErrorInfo): void {
    console.error('[OISM ErrorBoundary]', error, errorInfo);
  }

  handleReload = (): void => {
    this.setState({ hasError: false, errorMessage: null });
    window.location.reload();
  };

  handleGoHome = (): void => {
    this.setState({ hasError: false, errorMessage: null });
    window.location.href = '/';
  };

  render(): ReactNode {
    if (this.state.hasError) {
      return (
        <main className="access-page">
          <div className="access-card">
            <div className="brand-mark">O</div>
            <span className="access-icon">
              <AlertTriangle size={28} />
            </span>
            <h1>Đã xảy ra sự cố hiển thị</h1>
            <p>
              Hệ thống đã chặn lỗi phát sinh để bảo vệ phiên làm việc của bạn. Chi tiết kỹ thuật:{' '}
              <code>{this.state.errorMessage}</code>
            </p>
            <div style={{ display: 'flex', gap: '10px', justifyContent: 'center', marginTop: '12px' }}>
              <button
                type="button"
                className="button button--secondary"
                onClick={this.handleGoHome}
              >
                Về trang Tổng quan
              </button>
              <button
                type="button"
                className="button button--primary"
                onClick={this.handleReload}
              >
                <RefreshCw size={16} /> Tải lại ứng dụng
              </button>
            </div>
            <small>ErrorBoundary · OISM Web Admin React 19</small>
          </div>
        </main>
      );
    }

    return this.props.children;
  }
}
