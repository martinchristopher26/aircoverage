import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// Dev: `npm run dev` serves the SPA on :5173 and proxies /api -> the .NET API on
// HTTPS :8443 (run `dotnet run` in AirCoverage.Api separately). secure:false makes
// the proxy accept the self-signed dev cert; changeOrigin:false keeps the Host as
// localhost so the auth cookie maps correctly.
//
// NOTE: the API now sets a Secure auth cookie (HTTPS). For the login cookie to
// stick through this proxy, the Vite dev server must also be HTTPS — enable it
// with @vitejs/plugin-basic-ssl (see README). The Docker path serves the SPA and
// API same-origin over HTTPS, so it needs none of this.
// Build: outputs to web/dist, which the Dockerfile copies into the API's wwwroot.
export default defineConfig({
  plugins: [vue()],
  server: {
    port: 5173,
    proxy: {
      '/api': { target: 'https://localhost:8443', changeOrigin: false, secure: false },
    },
  },
  build: {
    outDir: 'dist',
    emptyOutDir: true,
  },
})
