// Floe · Tidepool — a tiny error reporter for the host probe.
// It is a classic script so it runs even when the module graph fails to load.
(function () {
  const view = window.chrome && window.chrome.webview;
  if (!view) return;

  function send(message) {
    try {
      view.postMessage({ type: 'error', phase: 'js', message: String(message) });
    } catch (_) {
      /* the bridge is gone */
    }
  }

  window.addEventListener('error', (event) => {
    send((event.message || 'script error') + ' @ ' + (event.filename || '?') + ':' + (event.lineno || 0));
  });

  window.addEventListener('unhandledrejection', (event) => {
    const reason = event.reason;
    send('unhandled rejection: ' + ((reason && reason.message) || reason));
  });
})();
