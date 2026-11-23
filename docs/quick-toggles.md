# 快捷开关插件

每个插件单独占一个目录。Quickfetch 打开快捷开关面板时会读取安装目录和当前用户目录里的清单。同一 ID 优先使用随程序安装的版本。

- 随程序安装的位置为 `plugins\toggles\<插件目录>\plugin.json`。
- 个人插件的位置为 `%LOCALAPPDATA%\Quickfetch\plugins\toggles\<插件目录>\plugin.json`。

面板打开时会运行插件的状态查询。安装个人插件前请确认脚本来源。面板关闭后停止查询，已经点选的操作会继续完成。

## 清单

```json
{
  "version": 1,
  "id": "my-switch",
  "title": "My switch",
  "titleZh": "我的开关",
  "icon": "◎",
  "script": "switch.ps1",
  "executablePicker": true,
  "actions": [{ "id": "reset", "title": "Reset", "titleZh": "重置" }]
}
```

`id` 以小写英文字母开头，可含小写英文字母、数字、点和连字符，最长 64 个字符。`script` 是同目录下的 Windows PowerShell 脚本文件名。`titleZh`、`icon`、`executablePicker` 和 `actions` 可省略。有程序位置或额外操作时，磁贴右侧显示进入选项面板的箭头。建议每项操作使用稳定、唯一的 ID。

## 脚本约定

第一个参数为 `status`、`on`、`off` 或 `action.<id>`。通过环境变量 `QUICKFETCH_TOGGLE_EXE` 读取用户所选的可执行文件路径，`QUICKFETCH_SHORTCUT_FOLDER` 提供应用快捷方式目录。状态查询只读。操作结束后，脚本向标准输出写入一条 UTF-8 JSON，以退出码 0 结束。

```json
{"on": true, "available": true, "note": "Connected"}
```

`on` 为布尔值，未知时可返回 `null`。`available: false` 禁用主开关，选项面板仍可打开。`note` 可显示状态详情。启用程序位置选择的插件可用可选字段 `executable` 返回自动发现的绝对路径，供下次冷启动。错误信息写入标准错误，退出码设为非零。状态查询限时 8 秒，切换和面板操作限时 45 秒。

## v2rayN

开启时，插件按需启动配置的 v2rayN，再选择其系统代理菜单中的 **Set system proxy**。关闭时选择 **Clear system proxy**。切换后会核对 v2rayN 的配置和 Windows 当前用户代理状态。程序位置优先使用已保存的路径，也可从运行中的 v2rayN、快捷方式目录中的 `v2rayN.lnk` 或 PATH 发现。

插件使用 Windows 桌面版 v2rayN 7.25.6 的系统代理选择器。托盘中的 v2rayN 可能会显示主界面。若 v2rayN 以管理员身份运行，切换时 Windows 可能请求授权。右侧箭头可手动更改程序位置。

## Windows 设备

点击 Wi-Fi、蓝牙或音量右侧的箭头，Windows 会直接显示网络、蓝牙设备或声音输出面板。Quickfetch 保留当前快捷开关页及滚动位置。关闭浮层后刷新对应开关或音量。在支持定位的 Windows 11 版本上，浮层对齐 Quickfetch 的开关面板，并从所点按钮的方向短距离滑入。关闭 Windows 动画效果时直接就位。关闭浮层后恢复系统位置。其他 Windows 版本由系统决定弹出位置。

电源模式的主按钮控制 Windows「节能模式」，高亮表示系统已启用节能。Quickfetch 通过 Windows 设置页的「始终使用节能模式」开关执行操作，随后读取系统的实际节能状态。系统的自动节能阈值继续生效。右侧箭头可设置插电或电池供电时的电源档位。亮度滑条使用设备的 WMI 背光接口。设备未提供此接口时，滑条会禁用。旋转锁定是否可用取决于设备与当前姿态。节能模式、旋转锁定和飞行模式操作时可能显示系统设置窗口。
