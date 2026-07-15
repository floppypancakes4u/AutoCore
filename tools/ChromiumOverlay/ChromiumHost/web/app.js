(function () {
  var stateEl = document.getElementById("state");
  var hpNums = document.getElementById("hpNums");
  var powerNums = document.getElementById("powerNums");
  var shieldNums = document.getElementById("shieldNums");
  var hpFill = document.getElementById("hpFill");
  var powerFill = document.getElementById("powerFill");
  var shieldFill = document.getElementById("shieldFill");

  function fmt(cur, max) {
    if (cur == null || cur < 0) return "—/—";
    if (max == null || max < 0) return String(cur) + "/—";
    return String(cur) + "/" + String(max);
  }

  function pct(cur, max) {
    if (cur == null || cur < 0 || max == null || max <= 0) return 0;
    var p = (100 * cur) / max;
    if (p < 0) p = 0;
    if (p > 100) p = 100;
    return p;
  }

  function applyPools(obj) {
    var hp = obj.hp;
    var maxHp = obj.maxHp;
    var power = obj.power;
    var maxPower = obj.maxPower;
    var shield = obj.shield;
    var maxShield = obj.maxShield;

    hpNums.textContent = fmt(hp, maxHp);
    powerNums.textContent = fmt(power, maxPower);
    shieldNums.textContent = fmt(shield, maxShield);

    hpFill.style.width = pct(hp, maxHp) + "%";
    powerFill.style.width = pct(power, maxPower) + "%";
    shieldFill.style.width = pct(shield, maxShield) + "%";

    var note = obj.hasVehicle
      ? "in vehicle"
      : "no vehicle (enter world / mount up)";
    stateEl.textContent =
      note +
      "  tick=" +
      (obj.tick != null ? obj.tick : "?") +
      "\n" +
      JSON.stringify(obj);
    stateEl.classList.toggle("muted", !obj.hasVehicle);
  }

  window.__applyGameState = function (jsonString) {
    try {
      var obj =
        typeof jsonString === "string" ? JSON.parse(jsonString) : jsonString;
      applyPools(obj || {});
    } catch (e) {
      stateEl.textContent = String(jsonString);
    }
  };

  setInterval(function () {
    if (
      window.__lastGameState &&
      window.__lastGameState !== window.__appliedSnapshot
    ) {
      window.__appliedSnapshot = window.__lastGameState;
      window.__applyGameState(window.__lastGameState);
    }
  }, 100);
})();
