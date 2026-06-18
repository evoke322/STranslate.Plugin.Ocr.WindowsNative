# Windows OCR — STranslate 插件

适用于 STranslate 的 Windows 系统 OCR 插件，调用 Windows 系统内置 OCR 引擎，完全离线、零依赖。

## ✨ 特性

- **零依赖** — 无需下载模型文件，无需配置 API Key
- **完全离线** — 所有识别在本地完成，图片不上传任何服务器
- **极速响应** — 通常 100ms 内完成识别
- **多语言** — 支持当前系统可用的 OCR 语言
- **位置信息** — 返回每行文字的边界框坐标（BoxPoints）

## 📦 安装

1. 下载最新的 `.spkg` 文件（在 Releases 页面）
2. 打开 STranslate → **设置** → **插件**
3. 将 `.spkg` 文件拖拽进窗口即可完成安装

## 📋 前置条件

- **操作系统**：Windows 10 版本 1809 (Build 17763) 或更高 / Windows 11
- **OCR 语言**：可用语言取决于当前系统提供的 OCR 能力

## ⚙️ 配置

在 STranslate 的插件设置页面中：

- **OCR 语言**：选择识别语言，默认为自动检测（使用系统用户首选语言）
- **刷新语言列表**：点击按钮可重新加载当前系统可用的 OCR 语言

## 🔧 使用方式

安装插件后，在 STranslate 的 OCR 服务中选择 **Windows OCR** 即可使用。支持截图识别、图片文件识别等所有 STranslate 的 OCR 场景。

## 📄 许可证

[MIT](LICENSE)
