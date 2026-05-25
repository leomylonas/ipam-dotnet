import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// https://vite.dev/config/
export default defineConfig({
	plugins: [react()],

	// Proxy API requests to the ASP.NET Core backend during development so
	// the Vite dev server and the API share the same origin — this keeps the
	// auth cookie's SameSite=Strict policy working without CORS configuration.
	server: {
		proxy: {
			'/api': { target: process.env.VITE_BACKEND_URL ?? 'http://localhost:5101', changeOrigin: true },
			'/auth': { target: process.env.VITE_BACKEND_URL ?? 'http://localhost:5101', changeOrigin: true },
			'/dashboard': { target: process.env.VITE_BACKEND_URL ?? 'http://localhost:5101', changeOrigin: true },
			'/health': { target: process.env.VITE_BACKEND_URL ?? 'http://localhost:5101', changeOrigin: true },
		},
	},

	css: {
		preprocessorOptions: {
			scss: {
				// Carbon v11 Sass sources still use @import internally; suppress the
				// legacy-import deprecation warning so the build log stays clean.
				silenceDeprecations: ['import', 'legacy-js-api'],
			},
		},
	},
});
