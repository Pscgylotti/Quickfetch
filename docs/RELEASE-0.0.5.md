# Quickfetch 0.0.5

新增独立网址模式、网址历史与网页检查缓存、中英文界面，以及离线字符映射和可拖动悬浮键盘。

- **网址模式**：主窗口按 **Alt+W**，直接输入网址或检索历史，不查询 Everything。输入裸域名可补全 HTTPS。上下键选择历史，**Tab** 补全，**Enter** 打开网页或将文件加入下载队列。文件模式中输入 HTTP(S) 网址也能显示历史。
- **历史与缓存**：仅在成功交给浏览器或下载器后记录网址和判断结果；按频率与最近使用排序，最多保存 256 项，界面最多显示 80 项。已确认的网页直接用浏览器打开，不重复发起下载类型检查；文件链接在再次下载前仍校验。设置中可清空历史与检查缓存，网址不会上传遥测。
- **English / 简体中文**：设置新增持久化的界面语言选择，主窗口即时切换；下载面板、通知、手势、文件操作、字符映射和键盘均提供英文文本。文件名、路径和用户输入保持原文。Edge / Chrome / Firefox 扩展弹窗与链接菜单也按浏览器语言提供英文。
- **字符映射**：扩展功能列表第一项，主窗口 **Alt+C** 打开。内置 Unicode 17 / CLDR 48 的 43,058 个字符与 emoji 序列，支持中文/英文名、U+ 编码、科学符号、拉丁与音标、希腊字母、假名和 emoji 搜索。23 种文字样式可整段或逐词预览，点击插入下方文本框，再复制或清空。
- **悬浮键盘**：字符映射沿用内置键盘偏好，支持鼠标与触屏拖动、Shift、左右光标、完整 emoji 退格、复制和换行；不抢输入位置，关闭后释放窗口。选择系统键盘时仍走 Windows 键盘路径。扩展按钮换为拼图图标。
- **性能优化**：字符卡片按可见行虚拟化，不预生成整套字形图片；索引只排序一次，缓存最近 8 次短查询，输入合并延迟降至 90 ms。本机同一基准中，字符索引保留的托管内存约 **21.3 → 13.6 MiB**，8 次不同查询约 **46 → 15 ms**；800 次缓存命中约 7 ms。数字为本机数据层测试结果，不是整个应用的内存或所有设备的速度保证。
- **下载记录**：已完成项目在列表和完成浮窗提供单独的“移除记录”，立即保存移除结果，保留文件。通知沿用主窗口亚克力主题；英文动作按钮避免挤占浮窗空间。

## Downloads and English quick start

Windows 11 x64 packages: **portable** includes .NET 10.0.13; **framework-dependent** requires .NET 10 Desktop Runtime. Open Settings → **Interface language → English**. Use **Win+S** to show Quickfetch, **Alt+W** for URL history, and **Alt+C** for Character Map. Arrow keys select a URL, Tab completes it, and Enter opens or downloads it. Previously confirmed webpages skip the classification request.

The release includes Edge, Chrome and Firefox extension ZIPs plus SHA-256 checksums for all five ZIPs. Register the native bridge from Quickfetch's download settings, then load the matching extension. Firefox remains an **unsigned temporary-load package**; permanent installation requires Mozilla signing. Existing HLS support requires local FFmpeg; live streams, DRM and separate audio tracks remain unsupported.

字符显示取决于系统字体；Unicode 缺失的样式保留原字符，不伪造字形。详见 [字符映射](character-map.md)、[网址与语言](urls-and-language.md)、[下载与扩展](downloads.md)。

MIT License。贡献者：Pscgylotti、Codex (OpenAI)、Claude (Anthropic)。
