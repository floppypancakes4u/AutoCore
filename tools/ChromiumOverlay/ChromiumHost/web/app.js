// Host injects game state via window.__applyGameState(jsonString) from C#.
// Also exposes a fallback poller if host only sets window.__lastGameState.

(function () {
  var el = document.getElementById("state");

  window.__applyGameState = function (jsonString) {
    try {
      var obj = typeof jsonString === "string" ? JSON.parse(jsonString) : jsonString;
      el.textContent = JSON.stringify(obj, null, 2);
    } catch (e) {
      el.textContent = String(jsonString);
    }
  };

  setInterval(function () {
    if (window.__lastGameState && window.__lastGameState !== window.__appliedSnapshot) {
      window.__appliedSnapshot = window.__lastGameState;
      window.__applyGameState(window.__lastGameState);
    }
  }, 100);
})();
