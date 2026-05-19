export const environment = {
  production: true,
  apiBase: '/api',
  wsBase: `${window.location.protocol === 'https:' ? 'wss' : 'ws'}://${window.location.host}`,
  assetsBase: '',
};
