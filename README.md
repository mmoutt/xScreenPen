# xScreenPen

一个轻量级的 Windows 屏幕画笔工具，支持鼠标、手指触控和触控笔。

[![Release](https://img.shields.io/badge/Release-v1.0.0-blue)](https://gitee.com/diao1548/x-screen-pen/releases)
[![License](https://img.shields.io/badge/License-MIT-green)](LICENSE)

## 下载安装

前往 [Releases](https://gitee.com/diao1548/x-screen-pen/releases) 页面下载最新版本：

- **xScreenPen.exe**

## 功能特性

- ✏️ **屏幕画笔** - 在屏幕任意位置自由绘制
- 🎨 **多种颜色** - 红、绿、蓝、黄、白、黑六种预设颜色
- 📏 **笔触大小** - 细、中、粗三种笔触可选
- 🧹 **橡皮擦** - 按笔迹擦除
- 🗑️ **清屏** - 一键清除所有笔迹
- 🖱️ **鼠标模式** - 穿透绘图层操作桌面
- 🔵 **悬浮球** - 点击展开/收起工具栏，拖动移动位置
- 📱 **多输入支持** - 完美支持鼠标、手指触控、触控笔

## 快捷键

| 快捷键 | 功能 |
|--------|------|
| `P` | 画笔模式 |
| `E` | 橡皮擦模式 |
| `M` | 鼠标模式 |
| `C` | 清屏 |
| `Esc` | 退出程序 |

## 系统要求

- Windows 7/8/10/11
- .NET Framework 4.7.2

## 编译

```bash
# 编译调试版本
dotnet build xScreenPen.sln

# 编译发布版本 (x64)
dotnet publish xScreenPen/xScreenPen.csproj -c Release -r win-x64 --self-contained false
```

## 运行

```bash
dotnet run --project xScreenPen/xScreenPen.csproj
```

或者直接运行编译后的 `xScreenPen.exe`

## 打包安装程序

1. 运行 PowerShell 构建脚本生成发布文件：
   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts/build-release.ps1
   ```

2. 使用 [Inno Setup](https://jrsoftware.org/isinfo.php) 编译安装程序：
   ```
   打开 scripts/installer.iss 并编译
   ```

## 许可证

MIT License

## 更新日志

详见 [CHANGELOG.md](CHANGELOG.md)
