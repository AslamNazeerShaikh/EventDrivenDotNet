import { Component, type ReactNode } from "react";

interface Props {
  children: ReactNode;
  label: string;
  // When this changes (for example fresh data arrived), a failed child is tried again.
  resetKey?: unknown;
}

// A render-time exception in one panel must not blank the page.
export class ErrorBoundary extends Component<Props, { failed: boolean }> {
  override state = { failed: false };

  static getDerivedStateFromError() {
    return { failed: true };
  }

  override componentDidUpdate(prev: Props) {
    if (this.state.failed && prev.resetKey !== this.props.resetKey)
      this.setState({ failed: false });
  }

  override render() {
    if (!this.state.failed) return this.props.children;
    return (
      <p
        role="alert"
        className="rounded border border-amber-400 bg-panel px-3 py-2 text-sm text-amber-300"
      >
        {this.props.label} could not be drawn. Retrying with the next update.
      </p>
    );
  }
}
