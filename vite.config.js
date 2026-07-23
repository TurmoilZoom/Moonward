import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// GitHub Pages project site: https://turmoilzoom.github.io/Moonward/
export default defineConfig({
  plugins: [vue()],
  base: '/Moonward/',
  build: {
    outDir: 'dist',
    assetsDir: 'assets',
    sourcemap: false,
  },
})
