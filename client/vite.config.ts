import { defineConfig } from "vitest/config";
import { loadEnv } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig(({ mode }) => {
  const environment = loadEnv(mode, ".", "");
  return ({
  plugins: [
    react(),
    {
      name: "systemiq-runtime-config",
      transformIndexHtml(html) {
        return html.replace(
          "</head>",
          '  <script src="/config.js"></script>\n  </head>',
        );
      },
    },
  ],
  build: {
    rollupOptions: {
      output: {
        manualChunks: {
          auth: ["@azure/msal-browser"],
          react: ["react", "react-dom"],
        },
      },
    },
  },
  server: {
    port: 5173,
    proxy: {
      "/api": {
        target: environment.SYSTEMIQ_API_PROXY_TARGET || "http://127.0.0.1:5080",
        changeOrigin: true,
      },
    },
  },
  test: {
    environment: "jsdom",
    setupFiles: "./src/test/setup.ts",
  },
  });
});
