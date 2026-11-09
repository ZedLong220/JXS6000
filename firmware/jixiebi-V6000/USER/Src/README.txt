========================================
JXS2000 机械臂 STM32F407VET6 移植说明
========================================

一、文件清单(都在 stm32_src/ 目录)
----------------------------------------
1. usart_retarget.c    -> 复制到 Core/Src/
   printf重定向到USART1

2. kinematics.h        -> 复制到 Core/Inc/
3. kinematics.c        -> 复制到 Core/Src/
   正运动学(4x4矩阵,从URDF参数)

4. can_protocol.h      -> 复制到 Core/Inc/
5. can_protocol.c      -> 复制到 Core/Src/
   CAN通信协议(对接CtrlStepDriver)

6. arm_controller.h    -> 复制到 Core/Inc/
7. arm_controller.c    -> 复制到 Core/Src/
   机械臂控制器(限位+穿地检查)

8. main_patch.c        -> 参考文件,不要直接编译
   把里面的代码片段粘贴到main.c对应USER CODE区域

9. it_patch.c          -> 参考文件,不要直接编译
   it.c基本不用改,回调函数在main_patch.c里


二、Keil工程配置
----------------------------------------
1. 添加源文件:
   Project面板右键Application/User/Core -> Add Existing Files
   选择: usart_retarget.c, kinematics.c, can_protocol.c, arm_controller.c

2. 添加头文件路径:
   Options for Target -> C/C++ -> Include Paths -> Add
   添加: ..\Core\Inc

3. 启用FPU:
   Options for Target -> Target -> Floating Point Hardware
   选择: Use Single Precision

4. 启用微库(printf需要):
   Options for Target -> Target -> 勾选 "Use MicroLIB"

5. 编译宏检查:
   Options for Target -> C/C++ -> Define
   确保有: USE_HAL_DRIVER, STM32F407xx
   (CubeMX默认已生成)


三、main.c修改(参考main_patch.c)
----------------------------------------
在main.c的对应USER CODE区域粘贴代码:

1. USER CODE BEGIN Includes:
   #include "can_protocol.h"
   #include "arm_controller.h"
   #include "kinematics.h"
   #include <stdio.h>

2. USER CODE BEGIN PV:
   static uint32_t ctrlTickCount = 0;
   static uint32_t lastPrintTick = 0;

3. USER CODE BEGIN 2 (main函数初始化):
   CAN_Proto_Init();
   Arm_Init();
   HAL_TIM_Base_Start_IT(&htim2);
   Arm_EnableAll();
   printf启动信息...

4. USER CODE BEGIN 3 (while循环):
   状态打印 + HAL_Delay(10)

5. USER CODE BEGIN 4 (回调函数):
   HAL_TIM_PeriodElapsedCallback()
   HAL_CAN_RxFifo0MsgPendingCallback()
   HAL_UART_RxCpltCallback()


四、硬件连接
----------------------------------------
STM32F407VET6          CtrlStepDriver(x6)
  PB8 (CAN1_RX)   <--->  CAN_H/CAN_L (通过CAN收发器)
  PB9 (CAN1_TX)   <--->  CAN_H/CAN_L
  GND             <--->  GND

  PA9  (USART1_TX) --->  USB-TTL调试输出
  PA10 (USART1_RX)

  PA2  (USART2_TX) --->  上位机通信(可选)
  PA3  (USART2_RX)

CAN总线两端需要120欧终端电阻!


五、关节地址分配
----------------------------------------
J1 = CAN节点1
J2 = CAN节点2
J3 = CAN节点3
J4 = CAN节点4
J5 = CAN节点5
J6 = CAN节点6


六、关节限位(度)
----------------------------------------
J1: -180 ~ 180
J2: 0    ~ 180  (正角向上)
J3: 0    ~ 180  (默认值180度)
J4: -90  ~ 90
J5: -90  ~ 90
J6: -180 ~ 180

动态穿地检查: 根据当前角度到目标角度的位移自动生成轨迹采样点,
每个中间姿态都重新进行正运动学并检查7个连杆包围盒最低点,
任一时刻Z<-1mm则在发送CAN运动指令前拒绝整条轨迹。
运动开始后还会周期读取6个电机的实际编码器位置并重新检测,
实际运动中若Z<-1mm会立即广播停止全部电机。


七、调试命令(通过USART1)
----------------------------------------
启动后会每秒打印一次状态:
- 当前6关节角度
- 末端位姿(XYZ + RPY)
- 穿地检查结果

串口命令以 \n 或 \r 结尾:
j1:0,j2:30,j3:150,j4:0,j5:-90,j6:0
home
stop
sethome
status 或 p
rpos
speed:20             设置后续关节位置运动默认速度为20度/秒
speed                查询当前默认关节运动速度
j2:50,speed:10       J2运动到50度, 本次速度临时使用10度/秒

单电机调试命令(id=1~6, id=0表示所有电机广播执行):
enable:id[,0/1]       电机使能/失能, 如 enable:1 或 enable:0,0
disable:id            电机失能, 如 disable:3
calib:id              电机校准, 如 calib:0
pos1:id               转到第一圈, 如 pos1:2
current:id,A          以A安培电流运行, 如 current:1,1.5
pos:id,rev            转到第rev圈, 如 pos:6,10
velocity:id,rad_s     以rad/s速度运行, 如 velocity:2,1.2

点位记录与循环运动(最多200个点位,上电自动从W25Q128载入):
point:set,1,j1:0,j2:30,j3:100,j4:0,j5:-90,j6:0
                      通过串口坐标设置P1到RAM，J1~J6必须全部提供
point:save,1          读取6个电机实际编码器角度并保存为RAM中的P1
point:get,1           查询P1
point:list            查询全部已记录点位
point:move,1          使用默认速度运动到P1
point:move,1,15       使用15度/秒运动到P1
point:clear,1         删除RAM中的P1，随后需point:store才会永久删除
point:clear,0         删除RAM中的全部点位，随后需point:store才会永久删除
point:store           把当前全部RAM状态（新增、修改、删除）保存到W25Q128
point:load            放弃未保存的RAM修改，从W25Q128重新载入
loop:1,3,5            按P1->P2->P3循环5次,使用默认速度
loop:3,1,5,20         按P3->P2->P1循环5次,速度20度/秒
loop                  查询循环状态
loop:stop             停止循环及当前运动; stop命令同样可以停止

循环运动为非阻塞执行,每个点位仍进行关节限位、完整轨迹穿地预检和
编码器实际位置动态穿地检测。循环期间可使用status、loop、loop:stop、stop。

W25Q128点位区固定为0x009FE000~0x009FFFFF（8KB）。驱动禁止写入
0x00A00000开始的6MB字库区。point:set/save/clear只修改RAM，必须执行
point:store才会写入Flash；保存后会回读并校验CRC32。


八、注意事项
----------------------------------------
1. 首次使用需要先给6个电机设零位(CAN_SetZero)
2. 减速比要和实际电机一致(默认J1=30,J2=50,J3=30,J4=30,J5=30,J6=1)
3. CAN波特率必须1Mbps(和CtrlStepDriver一致)
4. 穿地检查的特征点是从STL提取的包围盒角点,是近似值
5. 如果运动方向反了,修改can_protocol.c的CAN_SetPosition里的方向逻辑
