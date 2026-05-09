# Music Spectrum Analyzer

一个从准确频谱分析开始的音乐可视化原型。当前版本先收敛成单一的工程视图，后续再基于稳定分析结果做艺术化。

## 运行

直接打开 `index.html` 即可。也可以用本地服务器运行：

```powershell
node serve.mjs
```

然后访问：

```text
http://127.0.0.1:4173
```

## 使用

选择一个本地音频文件，点击“播放”。也可以点击“捕捉系统音频”，在浏览器弹出的共享窗口里选择带音频的屏幕、窗口或标签页，并开启共享音频。

没有音频文件时，可以点“演示信号”快速确认频谱绘制正常。

当前分析参数固定在代码里，避免过早引入复杂调节项：

- 8192 FFT
- 176 个对数频段
- 24Hz 到 18kHz
- -90dBFS 到 -12dBFS
- 上升/下落分离
- 峰值保持线

## 当前结构

- `index.html`：界面和控制区
- `styles.css`：布局和视觉样式
- `src/app.js`：WebAudio 分析和 Canvas 频谱渲染
- `serve.mjs`：零依赖本地静态服务器
- `VenomDesktop/`：Windows 桌面毒液应用

## 桌面版

桌面版是正式产品方向：透明点击穿透窗口、WASAPI loopback 系统音频捕捉、毒液球体和音频刺。

当前桌面版已改为非置顶，并尝试挂到 Explorer 桌面 WorkerW 层，避免遮挡普通工作应用。视觉也从外部线刺改成了由身体轮廓自然鼓出的音频突起。

构建：

```powershell
dotnet build .\VenomDesktop\VenomDesktop.csproj
```

运行：

```powershell
.\run_desktop.ps1
```

停止：

```powershell
.\stop_desktop.ps1
```
