# Quickfetch 0.0.8

## 快捷开关与扩展

- 主窗口新增快捷开关面板，可切换 Wi-Fi、蓝牙、夜间模式、旋转锁定、飞行模式和移动热点。下方可调亮度和音量，也可选择扬声器和麦克风。可用项取决于设备和 Windows 设置。
- 支持自定义开关和选项面板。随包的 v2rayN 插件支持指定程序路径、按需启动以及设置或清除系统代理。[插件格式和使用说明](https://github.com/Pscgylotti/Quickfetch/blob/0.0.8/docs/quick-toggles.md)
- 轻点拼图按钮可展开或收起扩展列表。按住并滑向轮盘中的字符映射、截图或下载，松手即可打开。

## 下载与文件搜索

- 在主窗口按 **Alt+D** 直接打开下载队列与历史。轻触面板右上角的 **−** 或按 **Esc** 返回，后台任务继续。**Alt+W** 仍打开网址输入。
- 文件搜索支持用左侧复选框独立勾选多项。轻触其他文件可继续浏览预览，勾选项保留。底部操作栏可复制、剪切或复制路径。
- 文件模式切回应用模式时保留查询中的英文字母和数字。字符映射结果列表改进了高精度触控板滚动。
- 文件预览优先读取 Windows 缩略图缓存，增加 Office、OpenDocument、压缩包及更多代码和配置文件格式的预览。

## 下载

Windows 11 x64 用户可选择包含 .NET 运行时的 **portable**，或选择需要 .NET 10 Desktop Runtime x64 的 **framework-dependent**。请完整解压，保留同目录的 `plugins` 和 `windows-ai-ocr`。Edge、Chrome、Firefox 扩展分别提供 ZIP，`SHA256SUMS.txt` 列出五个 ZIP 的校验值。

## English

Adds a Quick Settings panel with audio device selection and a v2rayN toggle plugin, a press-and-slide extension wheel, and **Alt+D** for download history. This release also fixes touch dismissal of the download panel, keeps checked files selected while browsing, and expands document, archive and source-file previews.
