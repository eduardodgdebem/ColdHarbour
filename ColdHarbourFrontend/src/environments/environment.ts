export const environment = {
  production: false,
  apiBase: '/api',
  // Derived at runtime so the same build works on localhost, a LAN IP, or a tunnel URL.
  wsBase: `${window.location.protocol === 'https:' ? 'wss' : 'ws'}://${window.location.host}`,
  assetsBase: '',
};
