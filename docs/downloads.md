# 后台下载与浏览器接管

文件模式输入 HTTP/HTTPS 网址（或 www. 开头的网址）后按 Enter，Quickfetch 读取响应头判断：网页交给默认浏览器，文件加入后台下载队列。输入过程中不预先联网。没有 Everything 也可以使用网址功能。右上角下载按钮集中查看队列、历史、进度与配置，主搜索窗口不显示常驻下载面板。

## 并发、队列与保存

- 常规文件至少 10 MiB 时默认 8 个异步 worker，每个从自己区间起点下载；空闲 worker 会再次切分仍在下载的最大剩余区间。worker 不是八个独立进程。
- 小于 10 MiB 且支持 Range 的文件先使用一个 worker，每三秒测速；低于 256 KiB/s 且剩余至少 1 MiB 时扩展为 8 个。服务器不支持 Range 时使用单路。
- 只运行一个下载任务，其他任务按顺序排队。暂停保留可视化，并取消当前连接，让下一项开始；所有项都暂停时不联网、不轮询。恢复的任务按恢复操作顺序排到队尾。
- 分段按偏移直接写入临时文件，正常下载不需要最后复制合并。每五秒活动检查点及暂停/退出时保存队列，完成文件不覆盖同名文件。恢复时复核长度、ETag/Last-Modified；源变化或不能验证的范围下载从头开始，非 Range 下载恢复时也需重下。
- 默认目录由 Windows Downloads Known Folder 获取，可以在下载面板中更改。新目录对新任务生效，已有任务仍使用其原目录。下载任务与 URL 保存在当前用户的本机 AppData，不上传遥测；带签名 URL 也可能包含在本机历史中。
- 代理选项为系统代理（.NET 默认代理与环境）、直连、自定义 HTTP/HTTPS/SOCKS5 代理。自定义地址不接受内嵌账号密码。配置在下一次任务启动或恢复时生效。

## 右下角浮窗

加入任务即显示右下角进度浮窗，浏览器接管不会打开主窗口。浮窗显示已下载区间、1–8 号 worker 位置及状态，可收起/展开，也可关闭；关闭只隐藏提示，不停止任务。同时显示至多两个浮窗，更多任务的提示排队。完成状态停留约 20 秒，鼠标停留时保留，历史可随时重新查看。

暂停、取消任务和关闭浮窗是不同操作。完成后的浮窗与历史列表提供打开文件、打开文件夹、删除文件；删除走 Windows 回收站。清空历史只移除已完成/已取消记录，不删除下载文件；失败项可继续，或先取消再清理。

## HLS/m3u8

需要本机已有 FFmpeg，下载设置可填写可执行文件路径，留空时从 PATH 查找。支持带结束标记的 HLS 点播、相对地址、主清单最高带宽变体、MPEG-TS/fMP4 初始化段、字节区间及 identity AES-128 CBC 分片。分片最多八路并行，暂停后保存已完成分片，恢复仅重取未完成分片；最终通过管道交给 FFmpeg 封装为 MP4，不让 FFmpeg 额外访问网络，网络请求都使用下载器代理。

暂不支持无结束标记的直播录制、独立外置音轨、多清晰度手选、DASH/MPD、DRM/SAMPLE-AES 或加密 fMP4 初始化段。超过 10,000 分片或特殊编码不能封装到 MP4 时显示错误，保留可重试的进度。没有 FFmpeg 时不假装完成。

## Edge、Chrome、Firefox 扩展

下载面板 → 展开配置 → **安装浏览器桥接 / 打开扩展目录**，会注册当前用户的 Native Messaging 主机并导出三个目录，不需要管理员权限。

- Edge：`edge://extensions` → 开发人员模式 → 加载解压缩的扩展 → 选择 `edge` 目录。
- Chrome：`chrome://extensions` → 开发者模式 → 加载已解压的扩展程序 → 选择 `chrome` 目录。
- Firefox：`about:debugging#/runtime/this-firefox` → 临时载入附加组件 → 选择 `firefox/manifest.json`。发行附件为未签名开发包，临时载入在浏览器重启后失效；标准 Firefox 的永久安装需要 Mozilla 签名，本发行版没有代称已上架商店。

Release 还提供独立浏览器扩展 ZIP：解压后对应 manifest 在根目录，也可以加载该目录。扩展本身不包含 Quickfetch EXE；必须先注册本机桥接。程序移动位置后需要重新注册。

默认自动接管 HTTP(S) 下载，可在扩展弹窗关闭，也可右键链接选择使用 Quickfetch 下载。原浏览器任务先暂停，Quickfetch 验证可下载并将任务写入持久队列后，扩展才取消原任务；拒绝或连接失败时尝试恢复原任务。下载已瞬间完成、blob/data、POST 专用链接、登录 Cookie/特定请求头保护的资源可能需留在浏览器。不会读取浏览器 Cookie 或密码，也不会承诺接管任意站点。

扩展仅有 downloads、nativeMessaging、contextMenus、storage 权限，无 `<all_urls>`、内容脚本或远程脚本。仅将请求的下载地址、文件名、来源 URL 和已知长度交给本机命名管道，管道限制当前用户。Firefox 按本机传递用途声明 browsingActivity/websiteActivity。相关接口：[Chromium 下载 API](https://developer.chrome.com/docs/extensions/reference/api/downloads)、[Edge Native Messaging](https://learn.microsoft.com/en-us/microsoft-edge/extensions-chromium/developer-guide/native-messaging)、[Firefox Native Messaging](https://developer.mozilla.org/en-US/docs/Mozilla/Add-ons/WebExtensions/Native_messaging)。
