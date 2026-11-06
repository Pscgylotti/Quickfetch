# 截图与 OCR

打开 Quickfetch 主窗口后按 **Alt+S**，或选择右上角 **扩展 → 截图 · OCR**（第二项）。主窗口和扩展菜单会先隐藏，再冻结整个桌面的画面。Alt+S 是主窗口内的快捷键，不占用其他程序的 Alt+S。

- **框选**：用鼠标或单指拖动需要的区域。
- **选择窗口**：指向窗口可预览其范围，点击确定。截图采用当前桌面的可见画面，被其他窗口挡住的部分仍会包含遮挡内容。
- **全屏**：选择所有显示器组成的整个桌面。

确定范围后，可取消选中、复制截图、另存为 PNG、保存到上一次成功保存的目录，或进入 **更多操作 · OCR**。第一次使用“保存到上次目录”会打开保存对话框。后续使用唯一文件名保存，目录偏好保存在设置中。保存取消或失败不会改变目录记录。Esc 首先取消范围，再次按下关闭截图。

更多操作页提供原图预览、缩放、复制与保存。点击 **识别文字 · OCR** 后，识别出的词段覆盖在图片对应位置，点击词段可选择或取消选择；支持多选、全选、取消选择，使用 **复制所选文字** 或 **Ctrl+C** 复制，并保留识别出的行顺序。没有文字选择时，Ctrl+C 复制截图。Ctrl+A 全选词段。

OCR 默认使用 **Microsoft.Windows.AI.Imaging.TextRecognizer**（Windows AI / NPU），采用与 TixTab / surfacetab 相同的准备和识别流程：初始 `NotReady` 时调用 `EnsureReadyAsync`，成功后创建识别器。此状态本身不代表不可用。不再调用传统 `Windows.Media.Ocr` 或用 PowerShell 执行识别；支持中英文混合内容，不受传统引擎单个语言包选择限制。错误时会明确显示 AI 后端错误，不会静默切回传统引擎。

识别是本地操作。截图通过内存与管道传给独立组件，不写临时图片文件，不上传图片或文字；仅主动保存才写 PNG。组件只在点击 OCR 时启动，预览中复用识别器，空闲 30 秒或关闭预览后释放。模型准备限时 3 分钟，识别限时 60 秒，关闭可立即取消。输入按需缩放到至多 400 万像素，返回的多边形范围转换回原截图像素后展示；保存与复制仍使用原始截图。截图本身限制为 8000 万桌面像素。安全桌面、受保护的视频等内容可能无法截取。

组件是 Quickfetch 自己的 `windows-ai-ocr/Quickfetch.WindowsAI.Ocr.exe`，不依赖 TixTab 安装目录或身份。它声明 `systemAIModels` 能力及独立 MSIX 身份。缺少身份时，预览页会提供“注册 AI 组件”。注册尊重既有系统策略，不开启开发者模式或导入证书。本机已有开发者模式时可开发注册；向普通用户正式分发需要受信任签名的 `Quickfetch.WindowsAI.Ocr.identity.msix`。识别还需要系统支持的 NPU、Windows 版本及系统 AI 模型。

构建组件：`powershell -File scripts/Build-WindowsAiOcr.ps1`，再构建主程序。运行时文件会随主程序的输出、发布和 ZIP 一起复制，许可证位于 `windows-ai-ocr/Licenses`。CI 构建组件但不在没有 NPU 的托管机器上运行模型。需要签名分发时，把与清单匹配的签名身份包放入组件运行时目录；当前开发构建不生成自签名证书。支持的部署应使用受信任发布证书签署身份包。

API 说明：[Windows AI OCR](https://learn.microsoft.com/en-us/windows/ai/apis/text-recognition)。

## English

While the Quickfetch window is open, press **Alt+S** or choose **Extensions → Screenshot · OCR**. Select a region, window, or the full desktop, then copy, save as PNG, save to the last folder, reselect, or open more tools. The preview supports zoom and local **Windows AI / NPU OCR**, using a separately identified Quickfetch companion. Tap recognized words to toggle selection; copy selected words with **Ctrl+C**, or select all with **Ctrl+A**. The recognizer is reused within the preview and released after 30 idle seconds or when the preview closes. Ordinary end-user distribution requires a trusted signed identity package; loose registration is only a development path on machines with Developer Mode already enabled.

截图预览还可点击 **识别二维码**。识别在本机完成，可复制结果；HTTP/HTTPS 链接可手动点击 **打开链接**，由默认浏览器打开。
