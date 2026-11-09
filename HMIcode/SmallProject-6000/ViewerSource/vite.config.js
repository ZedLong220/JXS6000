import { defineConfig } from 'vite'

export default defineConfig({
  base: '/',
  publicDir: 'public',
  build: {
    outDir: '../SmallProject/Viewer',
    emptyOutDir: true,
    assetsDir: 'assets'
  }
})
