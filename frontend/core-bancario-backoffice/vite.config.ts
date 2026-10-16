import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    // Debe coincidir con el redirectUri/webOrigin configurado en el cliente
    // 'core-bancario-frontend' del realm 'dev' de Keycloak.
    port: 3000,
  },
})
