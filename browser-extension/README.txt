Quickfetch Downloads / Edge, Chrome, Firefox

1. Run Quickfetch, open the top-right download button, expand download settings,
   and click Install browser bridge / Open extension directory.
2. Edge: edge://extensions -> Developer mode -> Load unpacked -> select edge/.
   Chrome: chrome://extensions -> Developer mode -> Load unpacked -> select chrome/.
   Firefox: about:debugging#/runtime/this-firefox -> Load Temporary Add-on ->
   select firefox/manifest.json. This release is unsigned; Firefox's temporary
   load ends at browser restart. Permanent installation requires Mozilla signing.
3. The popup checkbox enables/disables automatic download handoff. HTTP(S) only.
   Link context menu: Use Quickfetch to download. No launcher window is opened.

Release ZIPs contain the selected browser's manifest at their root. After step 1,
you may instead load that extracted ZIP directory. Native bridge registration
must point to the actual Quickfetch.exe you plan to keep, not a build/test copy.

Chromium extension ID: bbidpnpannkfhebdncjehboepamgobfm
Firefox add-on ID: quickfetch@pscgylotti
Native host: com.quickfetch.downloads

The browser task is paused while Quickfetch validates and saves its queue item.
Only successful handoff cancels the original task. Failure resumes the browser
task where supported. Completed, POST-only, blob/data and authenticated downloads
may need to stay in the browser. Cookies and browser passwords are not read.

Privacy: only requested download URLs, filenames, referring URL and expected
length are sent to the local Quickfetch native host. No telemetry, remote control
server, browsing history API, content scripts or remote JavaScript are used.
Quickfetch downloads from the selected source through its configured proxy.
Firefox declares browsingActivity/websiteActivity for this local handoff.

Closing a Quickfetch progress toast does not stop a task. Pause/cancel are
separate actions. Delete moves a completed file to the Windows Recycle Bin;
clearing completed/cancelled history does not remove downloaded files.
