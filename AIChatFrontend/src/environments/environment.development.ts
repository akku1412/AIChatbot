// Development settings, used by `ng serve`. Only PUBLIC values belong here.
export const environment = {
  production: false,
  // Same-origin path; proxy.conf.json forwards /api to ChatGPTClone.Api (https://localhost:7180).
  // Same origin is what lets the HttpOnly refresh cookie (SameSite=Strict) work.
  apiBaseUrl: '/api',
  // Google OAuth Web Client ID (public by design, not a secret).
  googleClientId: '901002317723-lk58p7huv06km29lnstieu4a8rbln0ok.apps.googleusercontent.com',
};
