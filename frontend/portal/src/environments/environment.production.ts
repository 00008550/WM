// Same-origin in production: nginx serves the SPA and proxies /api and /hubs
// to the API container, so no API URL is baked into the bundle.
export const environment = {
  apiUrl: '',
};
