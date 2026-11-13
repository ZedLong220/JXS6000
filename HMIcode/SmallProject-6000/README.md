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

## 六轴联动

配套仓库根目录的 `firmware/jixiebi-V6000` 主控和 `firmware/user-firmware-keil-57` 驱动。六块无刹车42步进驱动板使用同一个驱动HEX，拨码节点分别1～6，对应J1～J6；J6范围-180°～180°。

单点使用“六轴联动到目标”；记录至少两个点位后使用“六轴点位连续运动”。详细规划容差、循环方式、协议和验证结果见 `连续路径更新说明.md`。

2026-11-13 按现有六轴相线接法，所有轴相对原方向统一反向：J1～J6的模型角度到电机圈数方向为 `-/+/-/+/-/-`（原为 `+/-/+/-/+/+`）。方向集中在 `SmallProject/Devices/Arm/Kinematic/MotorCoordinateMap.cs`，构造motorJ后同时用于目标、路径速度和编码器反馈换算。仅更新上位机，无需换线或刷主控、驱动固件。URDF正方向、模型限位和零位设置方式保持一致。

更新后先用单轴小角度确认六个关节的实物方向。原版本记录的非零点位对应的电机目标方向已改变，需要重新核对或记录，不能直接按旧点位循环。

## 单轴运动

只接一块驱动板时，在右侧操作区下方选择“单轴：J1～J6”（默认J3），在上方对应的J轴输入框填写目标关节角度，再点击“单轴到目标”。角度是相对已设置零位的绝对目标，不是本次增量；保持目标不变再次点击不会再转同样角度。速度沿用系统设置，按减速比换算电机圈数。

单轴模式只等待所选节点的新编码器反馈，并通过协议4的参与轴位图只装载、启动该轴，例如J3为0x04。现有主控和驱动固件已支持，不需重新烧录。保留路径限位、碰撞、停止和最终编码器到位检查；非参与轴使用最近读取的位置，首次默认为模型0°，缺板时这些位置无法实测。六轴联动仍要求全部六个节点在线。

2026-11-13 启动检查优化：完整路径已覆盖首段时只检查一次；相邻区间不重复检查公共端点。碰撞粗筛改用缓存局部包围盒，精确表面相交仍使用原BVH；接近地面时回退到原顶点判断，采样间距和地面容差保持一致。130组实际模型姿态与原算法的结果一致。

本机离线基准：J3 10°检查由约1679ms降至约65ms，150°由约23811ms降至约369ms；发布版实际WPF/WebView检查分别为40ms和331ms。这些时间不含编码器查询、逐帧确认和驱动启动，实机总延迟以新增的路径准备分阶段日志为准。可在ViewerSource运行 `node scripts/benchmarkValidation.mjs` 复测，无串口或电机指令。

本机保留已生成的发布目录。Git只上传可构建源码、必要资源及预构建Viewer，不提交发布依赖、开发缓存、日志和个人运行数据。未使用的5000模型和临时PDF提取脚本已移出项目备份。

## PA0夹爪舵机

夹爪区使用主控PA0/TIM2_CH1的50Hz PWM，角度0°～180°对应500～2500µs。上电无脉冲，点击发送角度或0°/180°按钮后启用；可关闭PWM或查询状态。状态为指令角度和脉宽，不是舵机位置反馈。舵机按额定电压供电并与主控共地，PA0接信号线。机械臂停止保留夹爪当前PWM，关闭输出需要显式点击。

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
