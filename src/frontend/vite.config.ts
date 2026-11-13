/**
 * Vite configuration for the dashboard: the React and Tailwind plugins, and the dev-server proxy.
 *
 * It is read by `vite` (the `dev` script) and by `vite build` (the `build` script). It is not
 * part of the app bundle. In production there is no Vite: nginx in the dashboard container serves
 * the built files and forwards `/api/*` to the two API instances, which is what the dev proxy below
 * imitates.
 *
 * @module
 */
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import { defineConfig } from "vite";

/**
 * The Vite config object.
 *
 * @remarks
 * Dev proxy: the dashboard container publishes :4000 and forwards /api to the API.
 * So in development a request for `/api/overview` goes to `http://localhost:4000/api/overview`,
 * and the browser sees one origin, as it does in production.
 * `react()` enables the JSX build step and fast refresh. `tailwindcss()` turns the Tailwind classes
 * used in the source, and the theme in `src/index.css`, into CSS.
 */
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: { proxy: { "/api": "http://localhost:4000" } },
});
