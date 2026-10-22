# Quickfetch 0.0.4

新增后台下载、分段可视化和 Edge / Chrome / Firefox 下载接管扩展。

- **网址入口**：文件模式输入 HTTP/HTTPS 网址后按 Enter，网页在默认浏览器打开，文件进入后台下载队列；不依赖 Everything。
- **动态下载**：至少 10 MiB 的普通文件默认 8 worker 分段下载，先完成的 worker 动态拆分最大剩余区间。小于 10 MiB 先单 worker，测速较慢时再扩为 8 个；不支持 Range 时退回单路。校验 Content-Range 与源文件标识，支持安全续传、失败重试与持久队列。
- **队列和浮窗**：单任务运行、其余排队，暂停让出运行位置，恢复按操作顺序排队。右上角下载按钮查看历史与进度。右下角进度浮窗可收起、展开、关闭，关闭不停止下载，多个提示排队，浏览器接管不弹主窗口。
- **进度样式**：下载区间直接绘制在细进度条中，以竖条标注 worker 位置；浮窗沿用主窗口的亚克力材质、主题和按钮样式，收紧下方留白。
- **完成操作**：通知和历史均可打开文件、打开文件夹或移入回收站；清理历史不删除下载文件。
- **媒体流**：支持 HLS/m3u8 点播分片、主清单变体、相对地址、字节区间、fMP4 初始化段和 identity AES-128，已完成分片可保留续传，通过本机 FFmpeg 管道合并为 MP4。
- **目录和代理**：默认系统 Downloads，可配置目录、系统/直连/HTTP/HTTPS/SOCKS5 代理及 FFmpeg 路径。无任务时不增加下载轮询，只有打开的面板或浮窗刷新进度。
- **浏览器附件**：Release 一并提供 Edge、Chrome、Firefox 扩展 ZIP。先从 Quickfetch 下载设置安装本机桥接，再加载扩展；原浏览器任务仅在 Quickfetch 接收并保存成功后取消，失败时尝试恢复浏览器任务。

## 下载与限制

主程序提供 Windows 11 x64 portable（含 .NET 10.0.13）与 framework-dependent（需 .NET 10 Desktop Runtime），均附许可、贡献者和使用文档，所有 ZIP 由 SHA256SUMS.txt 覆盖。

扩展未上架商店。Edge/Chrome 为开发者模式加载包；Firefox 为**未签名的临时加载包**，永久安装需要 Mozilla 签名。扩展不读取 Cookie/密码，POST、blob/data、认证和已完成下载可能需由浏览器处理。

HLS 合并需要 FFmpeg，不捆绑、不自动下载。当前不支持直播录制、外置独立音轨、DASH/MPD、DRM/SAMPLE-AES；异常会保留可重试状态，不伪装为成功。无法验证或不支持 Range 的资源恢复时需要从头下载。

详见 [下载与浏览器扩展说明](downloads.md)。贡献者：Pscgylotti、Codex (OpenAI)、Claude (Anthropic)。MIT License。
