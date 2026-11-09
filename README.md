# JXS6000 五轴机械臂

JXS6000 上位机、STM32F407 主控、无刹车42步进闭环驱动固件及驱动板硬件工程。版本日期：2026-11-09。

## 目录

| 目录 | 内容 |
|---|---|
| `HMIcode/SmallProject-6000` | WPF上位机、JXS6000 URDF/STL、Viewer源码及离线页面、测试 |
| `firmware/jixiebi-V6000` | STM32F407主控，Keil ARMCC5工程 |
| `firmware/user-firmware-keil-57` | 无刹车42步进驱动，板载编码器闭环，Keil ARMCLANG6工程 |
| `firmware/hex` | 本次已编译的主控及驱动HEX |
| `hardware/driver-board` | 用户指定的嘉立创EDA `.epro2` 驱动板硬件工程 |

## 硬件与下载

PC的USB转CAN、F407主控和五块关节驱动板接入同一条1Mbps CAN总线。驱动板直接驱动电机，使用板载编码器，无机械刹车或外部DM542。

- 主控烧录 `firmware/hex/JXS6000-master.hex`，工程为 `firmware/jixiebi-V6000/MDK-ARM/jixiebi.uvprojx`，保留CMSIS-DAP设置。
- 五块相同驱动板均烧录 `firmware/hex/JXS6000-driver-42.hex`，各自拨码设为节点1～5，对应J1～J5，改拨码后重启。各板须保留自己的校准、电流和零位设置。
- J6仅用于模型兼容，固定0°，不生成节点6运动命令。
- 实际减速比和方向须与上位机配置一致；默认减速比30/150/150/100/100，方向+/-/+/-/+。

## 上位机

在 `HMIcode/SmallProject-6000` 执行：

```powershell
dotnet publish SmallProject/SmallProject.csproj -c Release -p:PublishProfile=win-x64-self-contained
```

运行生成的 `publish/win-x64-self-contained/SmallProject.exe`，复制到其他电脑时保留整个发布目录。需.NET 7 Windows构建环境和目标机WebView2 Runtime。测试工程使用.NET 10 SDK；程序并未改为Windows 7目标框架。

“五轴联动到目标”执行单点，“五轴点位连续运动”执行记录表顺序。全部路径预装驱动后统一启动，普通拐点按约1mm规划容差圆滑通过，首末点和精确反向停车。勾选循环会返回启动位置后重复，每圈末停车。1mm是规划几何界，不代表实机闭环误差。

原J2/J3固定点位保留，但在新模型中触发自碰撞，仍会被拦截。五轴操作使用新的五轴按钮。详见上位机的 `连续路径更新说明.md`。

修改Viewer后，在 `HMIcode/SmallProject-6000/ViewerSource` 执行 `npm ci` 和 `npm run build`。预构建Viewer已提交，可直接离线加载6000模型。

## 协议与验证

主机使用协议3、逐帧3B确认、显式参与轴位图（五轴1F，J2/J3为06）。五板逐段确认会话和装载数量后广播同步启动。任一参与节点故障或反馈超时，主控停止全部五轴；结束须全部编码器通过最终位置检查。

详细帧格式见主控 `README.md` 与驱动 `连续路径协议.md`。上位机、主控与驱动固件需配套更新。

电脑侧验证包括：2005项URDF/规划/协议检查，19项Viewer测试，2581项主控检查，以及实际HMI帧→主控源码→五个驱动执行器的27875项检查。后者验证同节拍结束、循环、故障、掉线和最终编码器偏差。发布版原生WPF/WebView2自检22项通过，两份Keil固件均0错误0警告。上述不等于五轴实机负载测试。

以下从仓库根目录运行上位机测试：

```powershell
dotnet run --project HMIcode/SmallProject-6000/Tests/KinematicsChecks.csproj
```

随后在ViewerSource运行 `npm test`。主控电脑侧测试在 `firmware/jixiebi-V6000/Tests`；驱动执行器测试在 `firmware/user-firmware-keil-57/Tests`。

## 文件清理

仓库不包含开发机Git历史、IDE缓存、bin/obj、node_modules、发布运行库、个人DATA、日志和旧5000模型。保留可构建源码、厂商库及其许可证、ONNX模型、6000三维资源、Keil工程和可烧录HEX。硬件 `.epro2` 按用户原文件完整复制，SHA-256记录见 `MANIFEST.sha256`。
