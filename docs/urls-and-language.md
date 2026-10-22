# 网址历史与界面语言

主窗口内 **Alt+W** 进入独立网址模式，不会向 Everything 发送查询。输入 `example.com/path`、`https://…` 或历史中的关键词；裸域名按 HTTPS 处理，仅允许 HTTP/HTTPS，不允许内嵌账号密码或本地文件协议。

上下键选择历史条目，**Tab** 将选中条目的完整网址填入输入框，不切换应用/文件模式。**Enter** 打开当前完整网址；输入内容只是历史关键词时，或已经用上下键/鼠标选择其他历史项时，打开所选项目。文件模式输入 HTTP(S) 网址时也可使用相同历史补全。网址模式中按转换按钮返回应用搜索。

首次打开未知网址会读取响应头以区分网页与文件；仅在浏览器启动请求或下载入队成功后保存记录。记录包含原始网址、网页/文件类型、显示名称、累计使用次数和最近使用权重，保存在 `%LOCALAPPDATA%\Quickfetch\url-history-v1.json`。频率权重按 7 天半衰期衰减，与应用启动排序使用相同策略；最多保存 256 项，列表展示前 80 项，不因输入字符写盘。

已记录为网页的网址再次打开会**直接启动浏览器，不重新进行网页/文件分类请求**。文件类型的历史仍会在新下载前校验响应，避免使用过期的分段与长度信息。网页类型缓存不设后台刷新；若链接用途变化，可以从设置中清空网址历史与缓存。带签名或私人路径的网址也可能保存在本机历史中；Quickfetch 不将历史上传遥测。

设置中的 **界面语言 / Interface language** 支持 **简体中文** 和 **English**，选择会保存到现有设置文件。主窗口、静态提示和托盘菜单即时切换，随后打开的工具窗口使用所选语言。文件名、路径、文件文本预览和输入的内容保持原文；Windows 自身生成的系统右键菜单跟随 Windows 的语言设置。浏览器扩展弹窗使用浏览器语言。

## English

Press **Alt+W** while Quickfetch is open to search URL history without Everything. Use arrow keys to select, **Tab** to complete and **Enter** to open a webpage or enqueue a file download. Previously confirmed webpages skip the classification request. **Settings → Interface language → English** enables the English interface and saves the preference. Clear URL history from Settings to remove both history and cached classifications.
