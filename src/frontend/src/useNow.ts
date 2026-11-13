/**
 * A React hook that gives a clock which ticks every second.
 *
 * It is used by `App.tsx`, which passes the value to the parts of the page that print "N s ago",
 * so those texts keep counting even when no new data arrives. It is a hook, not a pure function,
 * so it lives apart from `format.ts`. It does not call the API.
 *
 * @module
 */
import { useEffect, useState } from "react";

/**
 * Returns the current time and re-renders the calling component on a timer.
 *
 * @remarks
 * A clock that re-renders its component every second, so "N s ago" keeps counting when no data arrives.
 * The state starts from `Date.now` passed as a function, so it is read once, at mount. The interval is
 * cleared on unmount, and restarted if `intervalMs` changes.
 *
 * @param intervalMs - Time between ticks in milliseconds. Defaults to 1000 (1 s).
 * @returns The time of the latest tick, in epoch milliseconds.
 * @example
 * `const clock = useNow();` then `agoText(someIso, clock)` gives text that updates once a second.
 */
export function useNow(intervalMs = 1000): number {
  const [now, setNow] = useState(Date.now);
  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), intervalMs);
    // Cleanup: stop the timer when the component unmounts or the interval changes.
    return () => clearInterval(id);
  }, [intervalMs]);
  return now;
}
