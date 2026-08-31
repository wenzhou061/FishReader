# FishReader

FishReader 是一个面向 Windows 的轻量 TXT 悬浮阅读器。程序默认静默进入托盘，不会自动显示正文。

## 使用

1. 运行 `app\FishReader.exe`。
2. 右键系统托盘中的 FishReader 图标，选择“打开 TXT”。首次默认打开桌面，之后默认打开当前 TXT 所在目录。
3. 回到 Codex 或其他目标窗口，按 `Alt+B` 显示正文。
4. 切换、最小化或关闭当前窗口后，正文自动隐藏；切回时不会自动恢复。

程序已在后台运行时再次启动 `FishReader.exe`，会直接打开现有实例的设置窗口，不会创建重复进程。

## 快捷键

- `Alt+B`：显示或隐藏（老板键）
- `Alt+↓` / `Alt+↑`：向后或向前移动一行
- `Alt+PageDown` / `Alt+PageUp`：移动一屏
- `Alt+L`：解锁或锁定布局；解锁后拖动文字区域改变位置，拖右边缘调整宽度，拖下边缘增减行数，拖右下角同时调整
- `Alt+T`：切换深色/浅色页面配色

阅读器隐藏时只保留 `Alt+B`，不会占用其他阅读快捷键。

所有快捷键都可在“设置 → 快捷键”中修改。点击快捷键框后直接按下新组合键即可自动保存；`Esc` 取消，`Backspace` 恢复该项默认值。支持 `Ctrl` / `Alt` / `Shift` 组合、字母数字、方向键、`PageUp` / `PageDown`、`Home` / `End` 和 `F1`～`F12`；设置时会拒绝无修饰键的字母数字、无效格式和重复绑定。若组合键已被其他程序占用，托盘会提示注册失败。

## 设置和数据

- 双击托盘图标打开设置，也可以从右键菜单进入。
- 设置窗口分为“阅读与定位 / 外观 / 快捷键”，采用紧凑的深色卡片布局和 Windows 原生深色标题栏；默认尺寸为 `640 × 735`，三个页面均使用固定布局而非滚动页面。
- 宽度、行数、字号、行距、透明度、字体、字重和两套文字颜色均可调整；外观页提供实时模拟预览和完整深色样式的字重下拉框。
- “弱化行尾标点”会降低每个显示行末尾标点的透明度，不修改原文；鼠标停留在设置旁的 `?` 上会显示深色悬浮说明。
- “定位阅读位置”显示当前行数和百分比，支持百分比预览、向前/向后搜索；确认后才会跳转并立即保存。
- 配置和阅读进度保存在 `app\data\config.json`。

## 当前范围

- 只支持单个 `.txt` 文件。
- 自动识别带 BOM 的 UTF-8、UTF-16、UTF-32（LE/BE），识别无 BOM 的 UTF-8，并在必要时回退到 GB18030。
- 布局模式使用透明拖拽命中区和低对比度细线；拖动时显示当前宽度与行数。
- 不提供书架、封面、网络下载、自动翻页或开机自启。

## 开发环境

- Windows 10/11 x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- 项目不依赖第三方 NuGet 包

## 构建和验证

在项目根目录运行：

```powershell
dotnet build .\FishReader.csproj -c Release -warnaserror
dotnet run --project .\SmokeTests\SmokeTests.csproj -c Release
dotnet format .\FishReader.csproj --verify-no-changes
dotnet list .\FishReader.csproj package --vulnerable --include-transitive
```

生成 Windows x64 自包含单文件程序：

```powershell
dotnet publish .\FishReader.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -o .\app
```

## 仓库和发布文件

- `bin`、`obj`、`app` 和各级 `data` 目录均为本地生成物或用户数据，不进入 Git。
- `app\data\config.json` 可能包含本机 TXT 完整路径、阅读进度、窗口位置和快捷键设置，禁止提交。
- 自包含程序体积超过 GitHub 普通 Git 文件限制，应作为 GitHub Release 附件发布，不应提交到源码历史。

## License

本项目采用 [MIT License](LICENSE)。
