# Quickfetch 0.0.6

Quickfetch 是 Windows Search 的部分功能替代品：Windows Indexer 和 Windows Search 关闭后，仍可启动应用、通过 Everything 检索文件。

## 新增

- **截图与本地 OCR**：主窗口 Alt+S，或“扩展 → 截图 · OCR”。支持区域、窗口、全屏截图，复制、另存为、保存到上次目录；OCR 结果覆盖在图片上，可多选词段复制。
- **应用固定栏**：左侧只显示固定应用图标，支持快捷方式与 Windows 注册应用。触屏长按约 450ms、或鼠标拖动可调整顺序；图标跟随指针，列表预览插入位置。拖到屏幕顶部松手取消固定，Esc 或在其他区域松手撤销拖动。顺序在重启后保留。
- **三指/四指全局手势**：三指左/右/上/下滑默认对应 Alt+Tab、Alt+Shift+Tab、Win+Tab、Win+D；四指上/下滑可独立配置，默认未设置。三/四指可从任意位置开始，同向滑动至少对应屏幕尺寸的 8%（最少 64 DIP）。

## 改进与修复

- 媒体预览优先使用 Windows 缩略图缓存与系统生成的缩略图/专辑封面，先显示图片再补充属性；FFmpeg/FFprobe 仅作后备。缓存限制为 16 项 / 8 MiB，文件变化后失效。
- 切换应用/文件/网址模式时固定目标布局，减少窗口缩放中的文字重排与抖动。
- 应用切换到文件模式保留查询，网址模式 Tab 返回文件检索；网址 Shift+Enter 强制用浏览器打开并保存网页分类。
- 唤起时处理 Windows 开始菜单遮挡，恢复前台层级与输入焦点。
- 修复触屏选择结果后按内置键盘回车误移选项、扩展按钮重复点击重新展开等问题。
- 双指左右边缘内滑的距离改为屏幕宽度的 6%；顶部双指下滑保持顶部 6% 内起步、下滑至少高度的 8%。
- 快捷键手势串行执行，短暂等待系统释放上一组修饰键；错误提示列出仍被系统报告为按下的按键。提示改用应用浮窗，4 秒后淡出，不进入 Windows 通知中心。
- 修复固定栏右键菜单白底、下拉选项文字未垂直居中；界面文字统一为 Microsoft YaHei UI，避免文件名逐字混用不同字体和字重。
- Edge / Chrome / Firefox 下载扩展随版本一起提供。

## 下载

- `Quickfetch-0.0.6-win-x64-portable.zip`：包含 .NET 运行时，解压后运行 `Quickfetch.exe`。
- `Quickfetch-0.0.6-win-x64-framework-dependent.zip`：体积较小，需要 .NET 10 Desktop Runtime x64。
- 三种浏览器扩展为独立 ZIP；使用方法见 [下载说明](downloads.md)。
- `SHA256SUMS.txt` 提供各 ZIP 的 SHA-256 校验值。

请完整解压并保留 `windows-ai-ocr` 子目录；不要只复制单个 EXE 来使用 OCR。

## OCR 与兼容性

**OCR 是可选功能，不保证在所有 Windows 11 设备上可用。** 使用 Windows AI / NPU TextRecognizer，需要系统支持的 Windows 版本、NPU 和 AI 模型，以及组件的应用身份。当前发行包包含独立 OCR 组件和开发注册脚本，**不包含受信任签名的身份 MSIX**。开发注册仅适用于已启用开发者模式的设备；普通用户部署需要受信任签名身份包。Quickfetch 不会开启开发者模式、导入证书或更改系统策略。截图、保存与其他启动/搜索功能可独立使用。详见 [截图与 OCR](screenshot-and-ocr.md)。

系统视频缩略图不保证是第一帧；媒体格式支持取决于本机解码器。Everything 需独立安装并保持运行。全局手势使用并行 Raw Input 监听，不能保证阻止其他应用或 Windows 同时响应原始触摸。

## English

This release adds screenshot capture with optional local Windows AI / NPU OCR, an icon-only pinned-app rail with touch drag reordering and a top-edge unpin target, configurable three/four-finger gestures, faster system-cached media previews, smoother mode transitions, improved touch/foreground behavior, and consistent Microsoft YaHei UI typography. Gesture warnings now use a lightweight floating message that fades after four seconds.

Download the portable or framework-dependent Windows x64 package, plus the optional Edge / Chrome / Firefox extension. Keep the accompanying `windows-ai-ocr` directory. OCR requires supported hardware/models and application identity; this release does not include a trusted signed identity MSIX. Development registration requires Developer Mode to already be enabled. Capture and other launcher functions work independently of OCR.
