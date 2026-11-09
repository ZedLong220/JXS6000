urdf文件在D:\download\jixiebi\user_jixiebi\wangye\public\URDF

j1:0,j2:0,j3:180,j4:0,j5:-90,j6:0\n        # home
j1:0,j2:-30,j3:180,j4:0,j5:-90,j6:0\n      # 肩后仰30°
j1:0,j2:-60,j3:180,j4:0,j5:-90,j6:0\n      # 肩后仰60°
j1:0,j2:0,j3:150,j4:0,j5:-90,j6:0\n        # 肘前甩30°
j1:0,j2:-30,j3:140,j4:0,j5:-90,j6:0\n      # 后仰+前甩
j1:0,j2:-45,j3:130,j4:0,j5:-90,j6:0\n      # 更大动作
j1:0,j2:-60,j3:120,j4:0,j5:-90,j6:0\n      # 接近极限
j1:45,j2:-30,j3:150,j4:0,j5:-60,j6:90\n    # 综合姿态
j1:90,j2:-30,j3:150,j4:0,j5:-60,j6:0\n     # 转90°

home			#回零位
stop			#紧急停止
sethome			#设当前位置为零位
status			#打印本地状态（目标角度+末端位姿）
rpos			#实时读取电机真实位置
j1:0,j2:-30,j3:150,j4:0,j5:-90,j6:0,wait:10000
j1:90,j2:-30,j3:150,j4:50,j5:-60,j6:180,wait:100000
j1:0,j2:-60,j3:120,j4:0,j5:-90,j6:0,wait:15000
home,wait:15000

enable:1          使能1号电机
enable:0          使能全部电机
enable:0,0     // 失能全部电机
enable:0,1     // 使能全部电机
disable:3         失能3号电机
calib:0           校准全部电机
pos1:2            2号电机转到第一圈
current:1,1.5     1号电机以1.5A电流运行
pos:6,10          6号电机转到第10圈
velocity:0,1.2    全部电机以1.2 rad/s运行

设置参数
setcur:jN,电流A	设置电流限制	setcur:j1,2.0
setvel:jN,圈每秒	设置转速限制	setvel:j1,30
setacc:jN,圈每秒平方	设置加速度	setacc:j1,1000
setpid:jN,kp:值,kv:值,ki:值,kd:值	设置DCE PID	setpid:j1,kp:2000,kv:500,ki:0,kd:100

长电机：Kp：200，Kv：80，Ki：200，Kd：250
短电机：Kp：200，Kv：80，Ki：200，Kd：500

电机4（短电机）的PID参数：Kp：200，Kv：80，Ki：85，Kd：100
电机1（长电机）的PID参数：Kp：200，Kv：80，Ki：200，Kd：250

查询参数
getparam:jN	查询所有参数	getparam:j1
getcur:jN	查询电流限制	getcur:j1
getvel:jN	查询转速限制	getvel:j1
getpid:jN	查询DCE PID	getpid:j1

speed:20            设置默认运动速度为20°/s
speed               查询当前默认速度
j2:50               J2以当前默认速度运动到50°
j2:50,speed:10      J2以10°/s运动到50°，仅本次生效
home                按当前默认速度回零

velocity:2,1.2      2号电机以1.2 rad/s连续运行
velocity:0,1.2      全部电机以1.2 rad/s连续运行
velocity:2,0        2号电机速度设为0


point:save,1          保存当前位置为P1，读取实际编码器
point:save,2          保存当前位置为P2
point:save,3          保存当前位置为P3

point:get,1           查询P1
point:list            查询全部已保存点位

point:move,1          按默认速度运动到P1
point:move,1,15       以15°/s运动到P1

loop:1,3,5            P1→P2→P3循环5次
loop:3,1,5,20         P3→P2→P1以20°/s循环5次
loop                  查询当前循环状态
loop:stop             停止循环和当前运动
stop                  同样可以立即停止循环

# 输入点位坐标到 RAM
point:set,1,j1:0,j2:30,j3:100,j4:0,j5:-90,j6:0

# 读取实际编码器位置到 RAM
point:save,1

# 将全部 RAM 点位保存到 W25Q128
point:store

# 从 W25Q128 重新读取
point:load

# 查询
point:get,1
point:list

第一条只删除 RAM 中的点位，第二条才把删除结果写入 Flash，保证断电后仍然删除。
point:clear,1
point:store

移动到限位
limitl:1,2.0		/电机1以2.0rad/s的速度移动到限位
limitr:3
leftlimit:1,1.5	/电机1以1.5rad/s的速度移动到左限位
rightlimit:1,1.5