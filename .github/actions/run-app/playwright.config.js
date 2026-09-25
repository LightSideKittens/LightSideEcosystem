const webgpu = process.env.WEB_GRAPHICS_API === 'webgpu';

module.exports = {
  testDir: __dirname,
  use: {
    ignoreHTTPSErrors: true,
    ...(webgpu && {
      headless: false,
      launchOptions: {
        args: [
          '--enable-unsafe-webgpu',
          '--enable-features=Vulkan',
          '--use-vulkan=swiftshader',
          '--use-webgpu-adapter=swiftshader',
          '--use-angle=vulkan',
          '--disable-vulkan-surface',
          '--ignore-gpu-blocklist',
        ],
      },
    }),
  },
  timeout: parseInt(process.env.APP_TIMEOUT || '1800') * 1000,
};
