// Production settings. Only PUBLIC values belong here: this file ships to the browser.
// Never put API keys, SMTP passwords or JWT signing keys in any Angular file.
export const environment = {
  production: true,
  apiBaseUrl: '/api',
  // Google OAuth Web Client ID (public by design, not a secret).
  googleClientId: '901002317723-lk58p7huv06km29lnstieu4a8rbln0ok.apps.googleusercontent.com',
};
