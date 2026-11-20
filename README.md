# JXS6000 六轴机械臂

JXS6000 上位机、STM32F407 主控、无刹车42步进闭环驱动固件及驱动板硬件工程。版本日期：2026-11-20。

## 目录

| 目录 | 内容 |
|---|---|
| `HMIcode/SmallProject-6000` | WPF上位机、JXS6000 URDF/STL、Viewer源码及离线页面、测试 |
| `firmware/jixiebi-V6000` | STM32F407主控，Keil ARMCC5工程 |
| `firmware/user-firmware-keil-57` | 无刹车42步进驱动，板载编码器闭环，Keil ARMCLANG6工程 |
| `firmware/hex` | 本次已编译的主控及驱动HEX |
| `hardware/driver-board` | 用户指定的嘉立创EDA `.epro2` 驱动板硬件工程 |

## 硬件与下载

PC的USB转CAN、F407主控和六块关节驱动板接入同一条1Mbps CAN总线。驱动板直接驱动电机，使用板载编码器，无机械刹车或外部DM542。

- 主控烧录 `firmware/hex/JXS6000-master.hex`，工程为 `firmware/jixiebi-V6000/MDK-ARM/jixiebi.uvprojx`，保留CMSIS-DAP设置。
- 六块相同驱动板均烧录 `firmware/hex/JXS6000-driver-42.hex`，各自拨码设为节点1～6，对应J1～J6，改拨码后重启。各板须保留自己的校准、电流和零位设置。
- J6为实体关节，CAN节点6，角度范围-180°～180°。夹爪舵机独立使用主控PA0输出PWM。
- 实际减速比和方向须与上位机配置一致；默认减速比50/100/100/30/30/1，方向+/-/+/-/-/+。

## 上位机

在 `HMIcode/SmallProject-6000` 执行：

```powershell
dotnet publish SmallProject/SmallProject.csproj -c Release -p:PublishProfile=win-x64-self-contained
```

运行生成的 `publish/win-x64-self-contained/SmallProject.exe`，复制到其他电脑时保留整个发布目录。需.NET 7 Windows构建环境和目标机WebView2 Runtime。测试工程使用.NET 10 SDK；程序并未改为Windows 7目标框架。

“运动勾选轴”执行单点，“勾选轴点位连续运动”执行记录表顺序，支持1～6轴任意组合和部分轴点位记录。全部路径预装驱动后统一启动，圆滑范围默认20mm、可设1～50mm。循环以记录点闭合，从最近记录点进入，普通拐点和循环衔接尽量连续通过，精确原路折返仍需降速到零。圆滑范围是规划几何界，不代表实机闭环误差。

两处URDF分别位于 `SmallProject/Viewer/URDF/jxs6000.urdf` 和 `ViewerSource/public/URDF/jxs6000.urdf`（相对上位机目录），内容保持一致。J5限位为-95°～360°，给-90°目标保留边界余量；其余关节限位和模型结构保留。

原J2/J3固定点位保留，但在新模型中触发自碰撞，仍会被拦截。六轴操作使用新的六轴按钮。详见上位机的 `连续路径更新说明.md`。

修改Viewer后，在 `HMIcode/SmallProject-6000/ViewerSource` 执行 `npm ci` 和 `npm run build`。预构建Viewer已提交，可直接离线加载6000模型。

## PA0夹爪舵机

主控TIM2_CH1/PA0，50Hz，500～2500µs对应0°～180°。沿用指定HAL例程，仅启用PA0。上电不输出舵机脉冲；上位机夹爪区可发送角度、0°/180°、关闭PWM、查询状态。状态是已接受的指令值，没有舵机位置反馈。

舵机按额定电压单独供电并与主控共地，PA0接信号。机械臂停止保留当前夹爪PWM，关闭输出需显式操作。节点0命令55/DLC4角度float、56/DLC0关闭、57/DLC0查询；57响应为角度float、脉宽uint16、启用标志、版本1。

## 协议与验证

主机使用协议4、逐帧3B确认、显式参与轴位图（六轴3F，J2/J3为06）。参与驱动逐段确认会话和装载数量后广播同步启动；非循环路径在终点再广播同步收尾，使参与轴同时置零目标速度。任一参与节点故障或反馈超时，主控停止全部六轴；结束须全部参与轴编码器通过最终位置检查。正常结束、普通停止和主控故障停止保留健康驱动的位置保持力矩；显式“紧急失能”才关闭输出，本地驱动保护仍有效。

详细帧格式见主控 `README.md` 与驱动 `连续路径协议.md`。上位机、主控与六块驱动固件需配套更新到协议4，不与旧v3混用。J6默认减速比为1，请按实物修改。

本次电脑侧验证通过6728项URDF/规划/协议检查、23项Viewer模型与碰撞检查、4081项主控检查、实际HMI帧→主控源码→六个驱动执行器的24366项集成检查、70项驱动执行器检查和883560项三轴线帧循环检查。两份Keil固件均0错误0警告；WPF上位机完整重建0错误、242条警告。电脑侧检查不代替实机负载、同步停止误差和夹爪行程验证。

以下从仓库根目录运行上位机测试：

```powershell
dotnet run --project HMIcode/SmallProject-6000/Tests/KinematicsChecks.csproj
```

随后在ViewerSource运行 `npm test`。主控电脑侧测试在 `firmware/jixiebi-V6000/Tests`；驱动执行器测试在 `firmware/user-firmware-keil-57/Tests`。

## 当前固定取放路径

“执行固定取放路径”由主控执行，J1～J5参与，J6保持原位且不要求在线。启动前需五轴在线并位于零点：

1. 从 `(0,0,0,0,0,0)` 运动到 `(-15,70,-300,90,-90,0)`；J3经过约-240°时令舵机到90°。
2. 第一目标确认到位后令舵机回0°，等待1秒。
3. 仅J1运动到-50°，其余轴保持；到位后令舵机到90°，等待1秒。
4. J1～J5同步回零，回零轨迹时间为10秒；从实际同步启动计时，第5秒令舵机回0°。完成后继续保持位置力矩。

舵机角度是指令值，没有实际角度反馈。固定路径和普通路径期间由主控统一控制驱动，上位机刷新实际角度并限制冲突操作。本次同时修复首目标到位后卡在“到位校验”的流程切换。配套主控、驱动HEX位于 `firmware/hex`，SHA-256见 `MANIFEST.sha256`。

## 文件清理

仓库不包含开发机Git历史、IDE缓存、bin/obj、node_modules、发布运行库、个人DATA、日志和旧5000模型。保留可构建源码、厂商库及其许可证、ONNX模型、6000三维资源、Keil工程和可烧录HEX。硬件 `.epro2` 按用户原文件完整复制，SHA-256记录见 `MANIFEST.sha256`。
