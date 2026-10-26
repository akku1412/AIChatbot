// Applies the saved theme before Angular starts, so people who chose dark never see a white flash.
// A separate file (not an inline script) because the production Content-Security-Policy only allows 'self' scripts.
// ThemeService owns the theme once the app is running; the key and values must match it.
(function () {
  try {
    var theme = localStorage.getItem('aichat.theme');
    if (theme === 'light' || theme === 'dark') {
      document.documentElement.setAttribute('data-theme', theme);
    }
  } catch (e) {
    // Storage blocked (private mode, cookies off): follow the device's setting.
  }
})();
