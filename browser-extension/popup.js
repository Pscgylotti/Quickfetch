const api = globalThis.browser || globalThis.chrome;
const enabled = document.getElementById("enabled");
const english = !(navigator.language || "en").toLowerCase().startsWith("zh");
if (english) {
  document.documentElement.lang = "en";
  document.title = document.getElementById("title").textContent = "Quickfetch Downloads";
  document.getElementById("label").textContent = "Capture HTTP / HTTPS downloads";
  document.getElementById("status").textContent = "Loading…";
  document.getElementById("help").textContent = "The browser task is cancelled only after Quickfetch accepts it. Install the native bridge from Quickfetch's download panel and load the matching extension. Sign-in, POST and blob downloads may need the browser.";
}
api.storage.local.get({ enabled: true, lastResult: english ? "No downloads captured yet." : "尚无接管记录。" }).then(value => {
  enabled.checked = value.enabled;
  document.getElementById("status").textContent = value.lastResult;
});
enabled.addEventListener("change", () => api.storage.local.set({ enabled: enabled.checked }));
api.action.setBadgeText({ text: "" });
