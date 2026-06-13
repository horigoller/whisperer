import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// In dev, proxy /api to a locally running API (dotnet run in src/WhatsAppClient.Api).
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: { "/api": "http://localhost:5080" },
  },
  build: { outDir: "dist" },
});
