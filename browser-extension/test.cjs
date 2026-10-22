const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const test = require('node:test');
const source = fs.readFileSync(__dirname + '/background.js', 'utf8');
function environment({ ok = true, enabled = true, pauseFails = false, firefox = false } = {}) {
  const events = {}, calls = [];
  const api = {
    runtime: { id: 'fixture-extension', onInstalled: { addListener(fn) { events.install = fn; } }, async sendNativeMessage(host, request) { calls.push(['native', host, request]); return { ok, message: 'fixture' }; } },
    contextMenus: { create() {}, onClicked: { addListener(fn) { events.context = fn; } } },
    storage: { local: { async get() { return { enabled }; }, async set() {} } },
    action: { async setBadgeText() {} },
    downloads: { onCreated: { addListener(fn) { events.created = fn; } }, async pause(id) { calls.push(['pause', id]); if (pauseFails) throw Error('completed'); }, async cancel(id) { calls.push(['cancel', id]); }, async resume(id) { calls.push(['resume', id]); } }
  };
  const context = { chrome: api, console }; if (firefox) context.browser = api;
  vm.runInNewContext(source, context); return { events, calls };
}
test('successful handoff cancels the browser only after acceptance', async () => {
  const { events, calls } = environment(); await events.created({ id: 1, url: 'https://example.com/file.zip', filename: 'C:\\Downloads\\file.zip', totalBytes: 100 });
  assert.deepEqual(calls.map(c => c[0]), ['pause', 'native', 'cancel']); assert.equal(calls[1][2].expectedSize, 100); assert.equal(calls[1][2].name, 'file.zip');
});
test('rejected handoff resumes the original download', async () => {
  const { events, calls } = environment({ ok: false }); await events.created({ id: 2, url: 'https://example.com/login' });
  assert.deepEqual(calls.map(c => c[0]), ['pause', 'native', 'resume']);
});
test('blob and disabled capture do not modify browser downloads', async () => {
  const first = environment(); await first.events.created({ id: 3, url: 'blob:https://example.com/id' }); assert.equal(first.calls.length, 0);
  const second = environment({ enabled: false }); await second.events.created({ id: 4, url: 'https://example.com/file' }); assert.equal(second.calls.length, 0);
});
test('finished browser downloads are never duplicated in Quickfetch', async () => {
  const { events, calls } = environment({ pauseFails: true }); await events.created({ id: 5, url: 'https://example.com/file' }); assert.deepEqual(calls.map(c => c[0]), ['pause']);
});
test('Firefox browser namespace supports the same native bridge', async () => {
  const { events, calls } = environment({ firefox: true }); await events.created({ id: 6, url: 'https://example.com/movie.m3u8' }); assert.deepEqual(calls.map(c => c[0]), ['pause', 'native', 'cancel']);
});
