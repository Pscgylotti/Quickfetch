const api = globalThis.browser || globalThis.chrome;
const enabled = document.getElementById("enabled");
api.storage.local.get({ enabled: true, lastResult: "尚无接管记录。" }).then(value => {
  enabled.checked = value.enabled;
  document.getElementById("status").textContent = value.lastResult;
});
enabled.addEventListener("change", () => api.storage.local.set({ enabled: enabled.checked }));
api.action.setBadgeText({ text: "" });
