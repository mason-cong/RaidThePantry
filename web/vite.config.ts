import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// The dev server forwards /api to the backend, so the browser only ever talks
// to one origin and CORS never enters into it during development. The API's
// CORS policy still has to be right for production, where the two are served
// separately and there is no proxy in front of them.
const apiProxy = {
  '/api': {
    target: 'http://localhost:5282',
    changeOrigin: false,
  },
}

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: { port: 5173, proxy: apiProxy },
  // `vite preview` serves the built bundle and needs the same proxy. On a path
  // containing a "#" — as this repo has, under "C#" — the dev server cannot
  // resolve modules at all and preview is the only way to run the app. See the
  // README note.
  preview: { port: 4173, proxy: apiProxy },
})
