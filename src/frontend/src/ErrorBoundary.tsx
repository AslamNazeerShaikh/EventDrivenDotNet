/**
 * A React error boundary: keeps one failing part of the page from blanking the whole page.
 *
 * Where it sits: `main.tsx` wraps all of `App` in one, and `App.tsx` wraps each panel in its own.
 * It catches exceptions thrown while a child renders. It does not catch errors in event handlers
 * or in `fetch` calls; those are handled in `api.ts` and the panels. React requires a class for this,
 * so this is the one class component in the app. It does not call the API.
 *
 * @module
 */
import { Component, type ReactNode } from "react";

/**
 * Props of `ErrorBoundary`.
 */
interface Props {
  /** The content to protect. It is drawn as it is until it throws while rendering. */
  children: ReactNode;
  /** Name of the protected part, used in the fallback text: "<label> could not be drawn." */
  label: string;
  /**
   * When this changes (for example fresh data arrived), a failed child is tried again.
   * Compared with `!==`. Leave it out and the boundary never retries by itself.
   */
  resetKey?: unknown;
}

/**
 * Shows its children, or a one-line warning if a child threw while rendering.
 *
 * @remarks
 * A render-time exception in one panel must not blank the page.
 * After a failure it shows the warning until `resetKey` changes, then it renders the children again.
 * If they throw again, the warning comes back.
 *
 * @example
 * `<ErrorBoundary label="Chart" resetKey={chart.dataUpdatedAt}><ReadingsChart .../></ErrorBoundary>`
 */
export class ErrorBoundary extends Component<Props, { failed: boolean }> {
  /** Component state. `failed` is true while the fallback is shown. */
  override state = { failed: false };

  /**
   * React calls this when a descendant throws during render.
   *
   * @returns The state update that switches to the fallback. The error itself is not stored.
   */
  static getDerivedStateFromError() {
    return { failed: true };
  }

  /**
   * Clears the failed state when `resetKey` has changed, so the children are tried again.
   *
   * @param prev - The props from before this update.
   */
  override componentDidUpdate(prev: Props) {
    // Only retry when failed and the key moved; an unchanged key keeps the fallback.
    if (this.state.failed && prev.resetKey !== this.props.resetKey)
      this.setState({ failed: false });
  }

  /**
   * Draws the children, or the fallback paragraph when `failed` is true.
   *
   * @returns The children, or an amber `role="alert"` paragraph with the label.
   */
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
