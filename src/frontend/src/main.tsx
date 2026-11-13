/**
 * Entry point of the dashboard: mounts the React app into the page.
 *
 * Flow: `index.html` loads this file as a module. It creates the React Query client and renders
 * `App` into `<div id="root">`, wrapped from the outside in by `StrictMode`, `QueryClientProvider`
 * (so every hook in `api.ts` shares one cache), and an `ErrorBoundary`. This outermost boundary
 * catches a render failure in `App` itself; `App` adds one boundary per panel. It also imports
 * `index.css`, which brings in Tailwind and the theme.
 *
 * It exports nothing.
 *
 * @module
 */
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import App from "./App";
import { ErrorBoundary } from "./ErrorBoundary";
import "./index.css";

/**
 * The one React Query client for the page.
 *
 * @remarks
 * It uses the library defaults. The polling interval and the no-retry rule are set per query in
 * `api.ts` (the `poll` options), not here.
 */
const queryClient = new QueryClient();

// The "root" div is in index.html, so the non-null assertion holds.
createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <ErrorBoundary label="The dashboard">
        <App />
      </ErrorBoundary>
    </QueryClientProvider>
  </StrictMode>,
);
