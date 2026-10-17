# Quickfetch 0.0.1

Windows Search 的部分功能替代品。在 Windows Indexer 与 Windows Search 完全关闭的情况下，仍可启动开始菜单应用，并通过 Everything 查找文件；不会自动修改任何系统搜索服务。

- Win + S 快速打开，按名称、空格分词首字母、连续匹配及模糊重叠检索应用。
- 默认读取用户与公共开始菜单，结合最近使用及频率排序。
- Tab 切换 Everything 文件检索，支持名称、路径、大小、修改日期的服务端排序。
- Enter 打开、Shift+Enter 打开所在目录，提供原生经典 Shell 右键菜单。
- Acrylic 界面、中心展开动画、细滚动条；支持平板姿态、触屏键盘、整列启动键及可见插入光标。
- 图标与元数据打包缓存，目录通知合并后增量更新；隐藏时停止查询和光标定时器、释放结果列表。
- Everything 未运行时显示不可用状态及安装入口，应用检索保持可用。

## 下载

- **portable**：包含 .NET 10.0.13 运行时，Windows 11 x64，解压运行。
- **framework-dependent**：较小，需要 .NET 10 Desktop Runtime x64。
- 使用 `SHA256SUMS.txt` 校验发行包。

程序未签名。文件检索需安装并运行 Everything 1.4 或 1.5a。右键菜单为经典 Shell 样式；Acrylic 外观与平板姿态依赖系统支持。Quickfetch 不提供 Windows Search 的全部设置、网页、内容及语义检索功能。

本机完成匹配、IPC 协议、使用排序、图标持久缓存、增量目录变更、触屏编辑、Everything 实时查询/排序及 Shell 菜单生成检查。短时待机采样不能替代续航测试，内存和 I/O 测量条件见仓库 `docs/performance.md`。

Contributors: **Pscgylotti** 与 **Codex (OpenAI)**。MIT License。
