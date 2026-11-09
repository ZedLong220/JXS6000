# JXS6000 机械臂上位机

Windows WPF 上位机，包含串口 / CAN 控制、基于 URDF 的正逆运动学，以及与本地网页一致的三维预览与碰撞检测。

## 发布与运行

- 开发和发布电脑需要 .NET SDK；目标电脑不需要安装 .NET Runtime。
- 目标电脑仍需要 Microsoft Edge WebView2 Runtime。
- 在本目录运行：

```powershell
dotnet publish SmallProject/SmallProject.csproj -c Release -p:PublishProfile=win-x64-self-contained
```

发布结果位于 `publish/win-x64-self-contained`。复制到其他电脑时必须复制整个目录，不能只复制 `SmallProject.exe`。运行该程序启动上位机；添加 `--check-model` 参数可执行不连接硬件的模型自检。旧命令脚本已清理。

自包含发布已包含 .NET 7 x64 Desktop Runtime、Viewer、URDF/STL、图标、ONNX 模型和 NuGet 本机依赖。HALCON 功能仍需要目标电脑具备相应的 HALCON Runtime/授权；使用 ONNX GPU 功能时仍需要匹配的显卡驱动和 CUDA 运行环境。

`SmallProject/Viewer` 已包含可离线使用的三维页面和6000模型。修改页面源码后，在安装 Node.js 的机器运行：

```powershell
cd ViewerSource
npm ci
npm run build
```

模型坐标、零位、角度转换和碰撞边界见 `JXS6000适配说明.md`。

## 五轴联动

配套仓库根目录的 `firmware/jixiebi-V6000` 主控和 `firmware/user-firmware-keil-57` 驱动。五块无刹车42步进驱动板使用同一个驱动HEX，拨码节点分别1～5，对应J1～J5；J6虚拟固定0°。

单点使用“五轴联动到目标”；记录至少两个点位后使用“五轴点位连续运动”。详细规划容差、循环方式、协议和验证结果见 `连续路径更新说明.md`。

本机保留已生成的发布目录。Git只上传可构建源码、必要资源及预构建Viewer，不提交发布依赖、开发缓存、日志和个人运行数据。未使用的5000模型和临时PDF提取脚本已移出项目备份。

## 本机服务配置

服务凭据不随仓库分发：

- 小智接入点：环境变量 `XIAOZHI_MCP_ENDPOINT`，或运行目录的 `DATA/Config.data` 内的 `EndpointUrl`。
- 阿里云语音：环境变量 `DASHSCOPE_API_KEY`、`DASHSCOPE_WORKSPACE_ID`、`DASHSCOPE_APP_ID`。
- B 站直播参数默认留空，根据自身账号配置。

`DATA`、日志、IDE 缓存、构建产物和 `node_modules` 已排除提交。所需 ONNX 权重、URDF、STL 和预构建三维页面保留在仓库内。

## 验证

```powershell
dotnet run --project Tests/KinematicsChecks.csproj
cd ViewerSource
npm test
```

`Tests` 验证项目使用 .NET 10 SDK。WPF 程序还支持 `--check-model` 集成检查参数；该模式不连接串口或直播服务。
