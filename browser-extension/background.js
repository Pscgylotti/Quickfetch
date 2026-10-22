const api = globalThis.browser || globalThis.chrome;
const host = "com.quickfetch.downloads";
const pending = new Set();
const english = typeof navigator !== "undefined" && !(navigator.language || "en").toLowerCase().startsWith("zh");
api.runtime.onInstalled.addListener(() => {
  api.contextMenus.create({ id: "quickfetch-download", title: english ? "Download with Quickfetch" : "使用 Quickfetch 下载", contexts: ["link"] });
});
async function send(url, name, referer, expectedSize) {
  return api.runtime.sendNativeMessage(host, { url, name, referer, expectedSize });
}
api.contextMenus.onClicked.addListener(async (info, tab) => {
  if (info.menuItemId !== "quickfetch-download" || !/^https?:\/\//i.test(info.linkUrl || "")) return;
  try {
    const result = await send(info.linkUrl, null, tab?.url);
    await api.storage.local.set({ lastResult: result.message });
    if (!result.ok) await api.action.setBadgeText({ text: "!" });
  } catch (error) {
    await api.storage.local.set({ lastResult: String(error.message || error) });
    await api.action.setBadgeText({ text: "!" });
  }
});
api.downloads.onCreated.addListener(async item => {
  const { enabled = true } = await api.storage.local.get("enabled");
  const url = item.finalUrl || item.url;
  if (!enabled || item.byExtensionId === api.runtime.id || !/^https?:\/\//i.test(url) || pending.has(item.id)) return;
  pending.add(item.id);
  let paused = false;
  try {
    // A browser item must remain available until Quickfetch has durably accepted it.
    await api.downloads.pause(item.id); paused = true;
    const name = item.filename?.split(/[\\/]/).pop();
    const result = await send(url, name, item.referrer, item.totalBytes > 0 ? item.totalBytes : null);
    if (!result.ok) throw new Error(result.message);
    await api.downloads.cancel(item.id);
    await api.storage.local.set({ lastResult: result.message });
    await api.action.setBadgeText({ text: "" });
  } catch (error) {
    if (paused) {
      try { await api.downloads.resume(item.id); } catch { /* Already completed/interrupted items stay visible in Edge. */ }
    }
    await api.storage.local.set({ lastResult: (english ? "Not captured; keeping browser download: " : "未接管，保留浏览器下载：") + String(error.message || error) });
    await api.action.setBadgeText({ text: "!" });
  } finally { pending.delete(item.id); }
});
