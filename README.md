# Quickfetch

**Windows Search 的部分功能替代品：即使 Windows Indexer 与 Windows Search 已完全关闭，也可以快速启动应用并通过 Everything 查找文件。**

Windows 11 x64 · C# / WPF / .NET 10。无需 Electron、浏览器内核或 Windows 搜索索引。

## 下载与运行

在 [Releases](https://github.com/Pscgylotti/Quickfetch/releases) 下载 **0.0.1**：

- `Quickfetch-0.0.1-win-x64-portable.zip`：包含 .NET 运行时，解压运行 `Quickfetch.exe`。
- `Quickfetch-0.0.1-win-x64-framework-dependent.zip`：较小，需要 [.NET 10 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/10.0)。

启动后驻留托盘，**Win + S** 打开搜索。设置中可开启开机启动；默认关闭。托盘右键退出会释放快捷键。`--background` 仅在后台启动。

文件搜索可选依赖 [Everything](https://www.voidtools.com/downloads/)：支持 1.4 默认实例及 1.5a 实例，需在后台运行并启用 IPC。未检测到时显示“文件搜索不可用”及官方下载入口，应用启动仍可用。安装或启动后按 F5 重试。

## 操作

| 操作 | 功能 |
| --- | --- |
| Win + S | 打开窗口并清空当前搜索 |
| Tab / Shift + Tab | 切换模式；应用→文件保留输入，文件→应用清空 |
| ↑ / ↓ | 选择结果 |
| Enter / 双击 | 打开选中项 |
| Shift + Enter | 在资源管理器打开所在目录并选中文件 |
| 右键 / 菜单键 / Shift + F10 | 文件模式打开 Win11 风格菜单；“显示更多选项”打开完整 Shell 菜单 |
| F5 | 应用模式复核目录与图标；文件模式重新检索 |
| Esc / 点击窗口外部 | 隐藏窗口 |

右上角双箭头图标切换模式，设置按钮仅保留图标；打开快捷方式文件夹的按钮位于设置面板内，避免误触。文件模式窗口宽度为 784 DIP（相对原先缩小 20%）。文件列表显示名称、路径、大小和修改日期；点击列标题排序，拖动标题之间的分隔线调整相邻列宽，支持触屏并保存比例。排序由 Everything 对全部匹配项执行；每次加载 500 项，最多保留 5,000 项，更多结果请缩小查询范围。隐藏后重新打开会折叠设置面板。

文件结果可用鼠标拖出，触屏长按约 450ms 开始拖放，使用标准文件列表数据与接收文件的应用交互。拖动期间主窗口隐藏，保留虚化、可穿透的预览；屏幕顶部出现取消栏，拖入后松手或按 Esc 取消。文件菜单提供打开、打开目录、剪切、复制、复制路径、属性；第三方扩展通过“显示更多选项”进入系统经典菜单。触屏跨应用兼容性仍需按目标应用实际验证。

默认使用 Quickfetch 内置键盘：平板姿态下自动展开数字 / 错位 QWERTY 键盘，触摸搜索框也可展开；启动键独占右侧整列。文件模式提供空格及“打开目录”。光标跟随插入位置闪烁，点击文字末尾后的空白可定位到最后。物理键盘输入恢复正常编辑并收起内置键盘。

设置中取消勾选 **使用 Quickfetch 内置键盘** 后，搜索框保留正常编辑焦点，平板打开、触摸搜索框或点击键盘按钮时，改用 Windows 系统触摸键盘。通过窗口级 InputPane 请求显示/隐藏；系统策略或相关组件不可用时显示提示，不修改系统键盘服务。内置模式保持 WPF 隐式系统键盘唤起关闭，避免同时出现两套键盘。

## 全局触屏手势（main 分支新增）

设置 → **触屏手势…**，或右键托盘 → **触屏手势设置**。默认开启全局识别，隐藏到托盘后仍有效。顶部左半区 / 顶部右半区双指下滑、左边缘双指右滑、右边缘双指左滑分别支持配置快捷键或启动程序；动作默认未设置。勾选**启用双指右键**后，两指靠近轻点会在第一指的落点右击。

监听使用事件驱动 Raw Input，无后台轮询或触摸轨迹文件。可选择触屏所在显示器。原始触摸不会被拦截，部分应用及 Windows 边缘面板仍可能响应；管理员窗口、锁屏和 UAC 安全桌面受系统限制。详见 [手势设置、识别规则与验证范围](docs/touch-gestures.md)。此功能包含在 main 分支，已有 0.0.1 发行包保持原样。

## 应用来源与排序

默认通过系统 Known Folder API 定位**当前用户开始菜单**，并合并**公共开始菜单**，递归读取 `.lnk`。自选目录会保留，可关闭递归，不使用开发电脑的绝对路径。

应用模式输入英文、数字或拼音，不区分大小写，支持中文名称的全拼和首字母。匹配优先级：完整名称 / 完整单词 → 开头连续匹配 → 空格分词首字母 / 拼音首字母 → 中间连续匹配 → 最长公共连续子串 → 字符重叠。`Visual Studio Code` 可用 `vsc` / `vs`，`微信` 可用 `weixin` / `wx`，`QQ音乐` 可用 `qqyinyue` / `qqyy`。中文名称不会在扫描时被丢弃。

拼音由离线 TinyPinyin.Net 生成，按快捷方式对象缓存在内存中，输入时不访问磁盘或重复转换名称；移除的对象可随垃圾回收释放缓存。常见“音乐、银行、重庆”等词补充词组读音，其余多音字使用字库默认读音，不保证任意词组的读音推断。

文件模式在 Everything **1.5.0.1387+** 下，将普通英文/数字搜索词同时交给原文、全拼和首字母查询，使用 Everything 自身索引和排序，不另建全盘拼音索引，也不修改 Everything 设置。空格分词仍按 AND 组合。路径、引号、通配符、带冒号的高级语法和非拉丁查询保持原样；需显式拼音修饰符时可用 `pinyin-quan:` / `pinyin-jian:`。旧版 Everything 保留原有文件搜索，应用模式拼音不受影响。

默认列表结合频率与最近使用排序，使用权重以 7 天为半衰期衰减。有搜索词时仅调整同一匹配档位的顺序。只统计通过 Quickfetch 成功启动的应用，从首次使用开始积累，不读取其他软件的使用历史。

## 缓存与后台资源

- 没有周期性磁盘扫描。每次启动复核目录一次，发现离线期间的变化。
- `FileSystemWatcher` 通知经 650ms 合并，只更新受影响文件或子目录；通知溢出及手动 F5 才整目录复核。
- 单文件 `shortcuts-v1.json` 保存元数据与压缩 PNG 图标。未变化的图标直接恢复，不再请求 Shell；状态未变则不重写缓存。损坏、配置不匹配或过大的缓存会丢弃并重建。
- 每次输入使用内存目录与冻结位图，不读取快捷方式。隐藏时停止光标定时器，取消文件查询并释放结果列表。
- 不强制清空工作集，避免再次打开时额外换页。WPF / .NET 与图形资源仍占用内存；Everything 是独立进程，有自己的资源开销。

数据仅保存在 `%LOCALAPPDATA%\Quickfetch`：`settings.json`、`usage.json`、`shortcuts-v1.json`。使用记录只在成功启动时写入，不保存搜索词。快捷方式目标图标变化但 `.lnk` 未修改时，可用 F5 强制刷新。详见 [性能检查记录](docs/performance.md)。

## 退出问题与诊断

`%LOCALAPPDATA%\Quickfetch\diagnostics` 保存本机启动/退出原因与异常记录，不上传。`lifecycle.log` 与其 `.old` 备份各上限约 256 KiB；`running.json` 标记未完成的会话。只在启动、退出或异常时写入，没有日志轮询；不记录正常按键、触摸轨迹或搜索历史，错误信息中可能包含相关本地路径。

日志可以区分托盘主动退出、Windows 结束会话、已捕获异常，以及上次没有正常结束记录。仅有未完成标记不能区分任务管理器终止、更新替换、断电或崩溃。未知 UI / 运行时致命错误会记录后保留原有终止行为；可恢复的快捷方式刷新、文件搜索与触控回调错误在各自边界处理。日志不可写也不会因此退出。

电源暂停/恢复、锁定/解锁与会话连接通知也写入上述限量日志。暂停时释放输入监听并取消未完成的文件查询；恢复或解锁时重新绑定 Win + S 与触控监听，临近的重复通知会合并。此处理不请求唤醒、不阻止休眠、不修改 Modern Standby 策略，也不能在进程已被终止后自行运行。自动化验证使用合成通知和原生订阅检查，不会主动让电脑休眠；真实待机退出尚需现场日志确认。

exe 与窗口已嵌入搜索/闪电图标，包含 16–256 像素多尺寸。图标源文件为 `Quickfetch/Assets/Quickfetch.svg`，运行 `scripts/New-AppIcon.ps1` 可重新生成 ICO。

## 范围与限制

这是**部分替代**：应用启动与文件名 / 路径检索，不提供 Windows Search 的设置页、网页、内容索引、语义检索及全部系统集成。不会自动修改 Windows Search / Indexer 服务。

Win + S 使用只拦截该组合的专用键盘钩子，安全桌面等受保护场景不保证接管。毛玻璃使用原生 Acrylic 兼容接口，不可用时回退系统材质；系统版本、设置、远程桌面和节能状态可能影响外观。右键菜单是经典 Shell 样式，区别于 Windows 11 新式飞出菜单。程序未签名。

## 构建与检查

需要 Windows 与 .NET 10 SDK：

```powershell
dotnet build Quickfetch -c Release
dotnet run --project Quickfetch.Tests -c Release
dotnet run --project Quickfetch.IconTests -c Release
dotnet run --project Quickfetch.UiTests -c Release
# Everything 正在运行时执行实时集成检查：
dotnet run --project Quickfetch.UiTests -c Release -- --integration
# 生成两种发行包及 SHA256SUMS.txt：
pwsh -File scripts/Package.ps1
```

`Quickfetch.Core` 包含匹配、排序与 [Everything Query2 IPC 协议](https://www.voidtools.com/support/everything/sdk/ipc/)；`Quickfetch` 提供界面、Shell 集成与增量缓存。CI 不需要 Everything，不修改系统搜索设置。

第三方拼音组件版本和许可见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。文件拼音修饰符依据 [Everything 官方说明](https://www.voidtools.com/support/everything/search_modifiers/#pinyin)。

## Contributors

- **[Pscgylotti](https://github.com/Pscgylotti)** — 需求、设计决策、设备验证与维护。
- **Codex (OpenAI)** — AI 贡献者，参与实现、重构、检查及发布准备；发布经维护者授权。

详见 [CONTRIBUTORS.md](CONTRIBUTORS.md)。MIT License。
