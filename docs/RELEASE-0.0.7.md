# Quickfetch 0.0.7

## 新增与改进

- 截图预览增加离线二维码识别，可复制识别结果；HTTP/HTTPS 链接可手动点击打开，使用默认浏览器，不自动跳转。
- 文件搜索面板收起后重新打开，保留当前查询、搜索结果、选中项和滚动位置；自动全选搜索框，方便继续处理文件或输入新查询。此状态保留于当前运行会话，不写入长期历史。
- 降低结果列表触控板/滚轮滚动灵敏度，合并高精度输入，滚轮每格约 30 像素。
- 支持 Ctrl 点击多选、Shift 点击及 Shift+方向键连续选择；多选时预览显示数量与总大小。未知大小会注明，不递归扫描文件夹。
- Ctrl+C / Ctrl+X 对选中文件执行 Windows 复制/剪切，可在资源管理器粘贴；Ctrl+Shift+C 按行复制选中文件的完整路径。
- 遇到不兼容的 HTTP 分段响应时，停止全部 worker、丢弃部分数据，再从头尝试单路下载；不拼接不一致的内容。
- 下载弹窗中成功取消任务或删除文件后，立即关闭对应弹窗；失败时保留错误提示。

## 下载与兼容性

提供 Windows x64 便携版、依赖 .NET 10 Desktop Runtime 的较小版本、Edge / Chrome / Firefox 扩展及 SHA-256 校验文件。请完整解压，保留 windows-ai-ocr 子目录。

二维码识别在本地完成，不依赖 NPU 或 Windows AI OCR。Windows AI OCR 仍为可选功能，要求受支持的硬件、系统模型和组件身份；发行包不含受信任签名身份 MSIX，开发注册只适用于已启用开发者模式的设备。详见 [截图与 OCR](screenshot-and-ocr.md)。

## English

Adds offline screenshot QR decoding with explicit browser opening for HTTP(S) results, retained file-search sessions, gentler high-resolution scrolling, Ctrl/Shift multi-selection with size summaries, file copy/cut and copy-path shortcuts, clean single-stream fallback for incompatible range responses, and immediate dismissal of download popups after successful cancellation/deletion.
