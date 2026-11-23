namespace MotorControl
{
    partial class Form1
    {
        /// <summary>
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        /// <summary>
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            this.bt_Link = new System.Windows.Forms.Button();
            this.cb_ComList = new System.Windows.Forms.ComboBox();
            this.label1 = new System.Windows.Forms.Label();
            this.label1111 = new System.Windows.Forms.Label();
            this.lb_ID = new System.Windows.Forms.Label();
            this.bt_ReadInfo = new System.Windows.Forms.Button();
            this.lb_Pos = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.lb_Velocity = new System.Windows.Forms.Label();
            this.label5 = new System.Windows.Forms.Label();
            this.lb_Current = new System.Windows.Forms.Label();
            this.label7 = new System.Windows.Forms.Label();
            this.bt_PosOnce = new System.Windows.Forms.Button();
            this.bt_SpeedOnce = new System.Windows.Forms.Button();
            this.bt_InOnce = new System.Windows.Forms.Button();
            this.lb_LimitCurrent = new System.Windows.Forms.Label();
            this.label9 = new System.Windows.Forms.Label();
            this.label10 = new System.Windows.Forms.Label();
            this.bt_SetLimitCurrent = new System.Windows.Forms.Button();
            this.label13 = new System.Windows.Forms.Label();
            this.bt_SetPosCom = new System.Windows.Forms.Button();
            this.label14 = new System.Windows.Forms.Label();
            this.bt_Call = new System.Windows.Forms.Button();
            this.label15 = new System.Windows.Forms.Label();
            this.bt_SetVelocityCom = new System.Windows.Forms.Button();
            this.label16 = new System.Windows.Forms.Label();
            this.label17 = new System.Windows.Forms.Label();
            this.bt_SetCurrentCom = new System.Windows.Forms.Button();
            this.label18 = new System.Windows.Forms.Label();
            this.bt_OpenDocunment = new System.Windows.Forms.Button();
            this.numID = new System.Windows.Forms.NumericUpDown();
            this.numLimitCurrent = new System.Windows.Forms.NumericUpDown();
            this.num_Velocity = new System.Windows.Forms.NumericUpDown();
            this.num_Current = new System.Windows.Forms.NumericUpDown();
            this.bt_Enable = new System.Windows.Forms.Button();
            this.bt_StopNow = new System.Windows.Forms.Button();
            this.bt_ClearFault = new System.Windows.Forms.Button();
            this.num_Position = new System.Windows.Forms.NumericUpDown();
            this.list_Postions = new System.Windows.Forms.ListBox();
            this.label4 = new System.Windows.Forms.Label();
            this.bt_RecordPos = new System.Windows.Forms.Button();
            this.bt_LoopPos = new System.Windows.Forms.Button();
            this.lb_LimitVelocity = new System.Windows.Forms.Label();
            this.label8 = new System.Windows.Forms.Label();
            this.num_LimitVelocity = new System.Windows.Forms.NumericUpDown();
            this.bt_SetLimitVelocity = new System.Windows.Forms.Button();
            this.label6 = new System.Windows.Forms.Label();
            this.bt_SetThisIsHome = new System.Windows.Forms.Button();
            this.bt_SetKp = new System.Windows.Forms.Button();
            this.num_Kd = new System.Windows.Forms.NumericUpDown();
            this.num_Ki = new System.Windows.Forms.NumericUpDown();
            this.num_Kv = new System.Windows.Forms.NumericUpDown();
            this.num_Kp = new System.Windows.Forms.NumericUpDown();
            this.label11 = new System.Windows.Forms.Label();
            this.label19 = new System.Windows.Forms.Label();
            this.label21 = new System.Windows.Forms.Label();
            this.label23 = new System.Windows.Forms.Label();
            this.bt_ReSet = new System.Windows.Forms.Button();
            this.bt_SetKv = new System.Windows.Forms.Button();
            this.bt_SetKi = new System.Windows.Forms.Button();
            this.bt_SetKd = new System.Windows.Forms.Button();
            this.label12 = new System.Windows.Forms.Label();
            this.num_LoopSleepTime = new System.Windows.Forms.NumericUpDown();
            this.label20 = new System.Windows.Forms.Label();
            this.num_ACurrent = new System.Windows.Forms.NumericUpDown();
            this.bt_ACurrent = new System.Windows.Forms.Button();
            this.num_BCurrent = new System.Windows.Forms.NumericUpDown();
            this.bt_BCurrent = new System.Windows.Forms.Button();
            this.label22 = new System.Windows.Forms.Label();
            this.label24 = new System.Windows.Forms.Label();
            this.bt_DeleteLast = new System.Windows.Forms.Button();
            this.lb_LimitStatus = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.numID)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numLimitCurrent)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.num_Velocity)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.num_Current)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.num_Position)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.num_LimitVelocity)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.num_Kd)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.num_Ki)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.num_Kv)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.num_Kp)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.num_LoopSleepTime)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.num_ACurrent)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.num_BCurrent)).BeginInit();
            this.SuspendLayout();
            //
            // bt_Link
            //
            this.bt_Link.Location = new System.Drawing.Point(200, 24);
            this.bt_Link.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.bt_Link.Name = "bt_Link";
            this.bt_Link.Size = new System.Drawing.Size(108, 30);
            this.bt_Link.TabIndex = 0;
            this.bt_Link.Text = "连接";
            this.bt_Link.UseVisualStyleBackColor = true;
            this.bt_Link.Click += new System.EventHandler(this.bt_Link_Click);
            //
            // cb_ComList
            //
            this.cb_ComList.FormattingEnabled = true;
            this.cb_ComList.Location = new System.Drawing.Point(73, 28);
            this.cb_ComList.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.cb_ComList.Name = "cb_ComList";
            this.cb_ComList.Size = new System.Drawing.Size(121, 23);
            this.cb_ComList.TabIndex = 1;
            this.cb_ComList.DropDown += new System.EventHandler(this.cb_Com_DropDown);
            //
            // label1
            //
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(28, 31);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(39, 15);
            this.label1.TabIndex = 2;
            this.label1.Text = "COM:";
            //
            // label1111
            //
            this.label1111.AutoSize = true;
            this.label1111.Location = new System.Drawing.Point(28, 110);
            this.label1111.Name = "label1111";
            this.label1111.Size = new System.Drawing.Size(68, 15);
            this.label1111.TabIndex = 3;
            this.label1111.Text = "电机ID：";
            //
            // lb_ID
            //
            this.lb_ID.AutoSize = true;
            this.lb_ID.Location = new System.Drawing.Point(189, 110);
            this.lb_ID.MinimumSize = new System.Drawing.Size(149, 0);
            this.lb_ID.Name = "lb_ID";
            this.lb_ID.Size = new System.Drawing.Size(149, 15);
            this.lb_ID.TabIndex = 4;
            this.lb_ID.Text = "（空）";
            this.lb_ID.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // bt_ReadInfo
            //
            this.bt_ReadInfo.Location = new System.Drawing.Point(31, 70);
            this.bt_ReadInfo.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.bt_ReadInfo.Name = "bt_ReadInfo";
            this.bt_ReadInfo.Size = new System.Drawing.Size(111, 30);
            this.bt_ReadInfo.TabIndex = 5;
            this.bt_ReadInfo.Text = "读取信息";
            this.bt_ReadInfo.UseVisualStyleBackColor = true;
            this.bt_ReadInfo.Click += new System.EventHandler(this.bt_ReadInfo_Click);
            //
            // lb_Pos
            //
            this.lb_Pos.AutoSize = true;
            this.lb_Pos.Location = new System.Drawing.Point(189, 128);
            this.lb_Pos.MinimumSize = new System.Drawing.Size(149, 0);
            this.lb_Pos.Name = "lb_Pos";
            this.lb_Pos.Size = new System.Drawing.Size(149, 15);
            this.lb_Pos.TabIndex = 7;
            this.lb_Pos.Text = "（空）";
            this.lb_Pos.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // label3
            //
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(28, 128);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(136, 15);
            this.label3.TabIndex = 6;
            this.label3.Text = "电机位置（rev）：";
            //
            // label2
            //
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(595, 19);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(68, 15);
            this.label2.TabIndex = 9;
            this.label2.Text = "电机ID：";
            //
            // lb_Velocity
            //
            this.lb_Velocity.AutoSize = true;
            this.lb_Velocity.Location = new System.Drawing.Point(189, 146);
            this.lb_Velocity.MinimumSize = new System.Drawing.Size(149, 0);
            this.lb_Velocity.Name = "lb_Velocity";
            this.lb_Velocity.Size = new System.Drawing.Size(149, 15);
            this.lb_Velocity.TabIndex = 11;
            this.lb_Velocity.Text = "（空）";
            this.lb_Velocity.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // label5
            //
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(28, 146);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(138, 15);
            this.label5.TabIndex = 10;
            this.label5.Text = "电机速度(rev/s)：";
            //
            // lb_Current
            //
            this.lb_Current.AutoSize = true;
            this.lb_Current.Location = new System.Drawing.Point(189, 164);
            this.lb_Current.MinimumSize = new System.Drawing.Size(149, 0);
            this.lb_Current.Name = "lb_Current";
            this.lb_Current.Size = new System.Drawing.Size(149, 15);
            this.lb_Current.TabIndex = 13;
            this.lb_Current.Text = "（空）";
            this.lb_Current.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // label7
            //
            this.label7.AutoSize = true;
            this.label7.Location = new System.Drawing.Point(28, 164);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(114, 15);
            this.label7.TabIndex = 12;
            this.label7.Text = "电机电流(mA)：";
            //
            // bt_PosOnce
            //
            this.bt_PosOnce.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.bt_PosOnce.Location = new System.Drawing.Point(594, 113);
            this.bt_PosOnce.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.bt_PosOnce.Name = "bt_PosOnce";
            this.bt_PosOnce.Size = new System.Drawing.Size(179, 30);
            this.bt_PosOnce.TabIndex = 14;
            this.bt_PosOnce.Text = "转到第一圈";
            this.bt_PosOnce.UseVisualStyleBackColor = true;
            this.bt_PosOnce.Click += new System.EventHandler(this.bt_PosOnce_Click);
            //
            // bt_SpeedOnce
            //
            this.bt_SpeedOnce.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.bt_SpeedOnce.Location = new System.Drawing.Point(594, 146);
            this.bt_SpeedOnce.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.bt_SpeedOnce.Name = "bt_SpeedOnce";
            this.bt_SpeedOnce.Size = new System.Drawing.Size(179, 30);
            this.bt_SpeedOnce.TabIndex = 15;
            this.bt_SpeedOnce.Text = "以1rev/s的速度旋转";
            this.bt_SpeedOnce.UseVisualStyleBackColor = true;
            this.bt_SpeedOnce.Click += new System.EventHandler(this.bt_SpeedOnce_Click);
            //
            // bt_InOnce
            //
            this.bt_InOnce.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.bt_InOnce.Location = new System.Drawing.Point(594, 180);
            this.bt_InOnce.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.bt_InOnce.Name = "bt_InOnce";
            this.bt_InOnce.Size = new System.Drawing.Size(179, 30);
            this.bt_InOnce.TabIndex = 16;
            this.bt_InOnce.Text = "以1A的电流旋转";
            this.bt_InOnce.UseVisualStyleBackColor = true;
            this.bt_InOnce.Click += new System.EventHandler(this.bt_InOnce_Click);
            //
            // lb_LimitCurrent
            //
            this.lb_LimitCurrent.AutoSize = true;
            this.lb_LimitCurrent.Location = new System.Drawing.Point(189, 182);
            this.lb_LimitCurrent.MinimumSize = new System.Drawing.Size(149, 0);
            this.lb_LimitCurrent.Name = "lb_LimitCurrent";
            this.lb_LimitCurrent.Size = new System.Drawing.Size(149, 15);
            this.lb_LimitCurrent.TabIndex = 18;
            this.lb_LimitCurrent.Text = "（空）";
            this.lb_LimitCurrent.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // label9
            //
            this.label9.AutoSize = true;
            this.label9.Location = new System.Drawing.Point(28, 182);
            this.label9.Name = "label9";
            this.label9.Size = new System.Drawing.Size(144, 15);
            this.label9.TabIndex = 17;
            this.label9.Text = "电机限制电流(mA)：";
            //
            // label10
            //
            this.label10.AutoSize = true;
            this.label10.Location = new System.Drawing.Point(29, 356);
            this.label10.Name = "label10";
            this.label10.Size = new System.Drawing.Size(144, 15);
            this.label10.TabIndex = 19;
            this.label10.Text = "设置限制电流(mA)：";
            //
            // bt_SetLimitCurrent
            //
            this.bt_SetLimitCurrent.Location = new System.Drawing.Point(290, 351);
            this.bt_SetLimitCurrent.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.bt_SetLimitCurrent.Name = "bt_SetLimitCurrent";
            this.bt_SetLimitCurrent.Size = new System.Drawing.Size(100, 30);
            this.bt_SetLimitCurrent.TabIndex = 21;
            this.bt_SetLimitCurrent.Text = "设置";
            this.bt_SetLimitCurrent.UseVisualStyleBackColor = true;
            this.bt_SetLimitCurrent.Click += new System.EventHandler(this.bt_SetLimitCurrent_Click);
            //
            // label13
            //
            this.label13.AutoSize = true;
            this.label13.Location = new System.Drawing.Point(610, 372);
            this.label13.Name = "label13";
            this.label13.Size = new System.Drawing.Size(22, 15);
            this.label13.TabIndex = 29;
            this.label13.Text = "圈";
            //
            // bt_SetPosCom
            //
            this.bt_SetPosCom.Location = new System.Drawing.Point(638, 366);
            this.bt_SetPosCom.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.bt_SetPosCom.Name = "bt_SetPosCom";
            this.bt_SetPosCom.Size = new System.Drawing.Size(136, 30);
            this.bt_SetPosCom.TabIndex = 28;
            this.bt_SetPosCom.Text = "发送命令";
            this.bt_SetPosCom.UseVisualStyleBackColor = true;
            this.bt_SetPosCom.Click += new System.EventHandler(this.bt_SetPosCom_Click);
            //
            // label14
            //
            this.label14.AutoSize = true;
            this.label14.Location = new System.Drawing.Point(469, 371);
            this.label14.Name = "label14";
            this.label14.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            this.label14.Size = new System.Drawing.Size(52, 15);
            this.label14.TabIndex = 26;
            this.label14.Text = "转到第";
            //
            // bt_Call
            //
            this.bt_Call.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.bt_Call.Location = new System.Drawing.Point(594, 80);
            this.bt_Call.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.bt_Call.Name = "bt_Call";
            this.bt_Call.Size = new System.Drawing.Size(179, 30);
            this.bt_Call.TabIndex = 30;
            this.bt_Call.Text = "电机校准";
            this.bt_Call.UseVisualStyleBackColor = true;
            this.bt_Call.Click += new System.EventHandler(this.bt_Call_Click);
            //
            // label15
            //
            this.label15.AutoSize = true;
            this.label15.Location = new System.Drawing.Point(525, 403);
            this.label15.Name = "label15";
            this.label15.Size = new System.Drawing.Size(107, 15);
            this.label15.TabIndex = 34;
            this.label15.Text = "rev/s速度运行";
            //
            // bt_SetVelocityCom
            //
            this.bt_SetVelocityCom.Location = new System.Drawing.Point(638, 397);
            this.bt_SetVelocityCom.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.bt_SetVelocityCom.Name = "bt_SetVelocityCom";
            this.bt_SetVelocityCom.Size = new System.Drawing.Size(136, 30);
            this.bt_SetVelocityCom.TabIndex = 33;
            this.bt_SetVelocityCom.Text = "发送命令";
            this.bt_SetVelocityCom.UseVisualStyleBackColor = true;
            this.bt_SetVelocityCom.Click += new System.EventHandler(this.bt_SetVelocityCom_Click);
            //
            // label16
            //
            this.label16.AutoSize = true;
            this.label16.Location = new System.Drawing.Point(422, 403);
            this.label16.Name = "label16";
            this.label16.Size = new System.Drawing.Size(22, 15);
            this.label16.TabIndex = 31;
            this.label16.Text = "以";
            //
            // label17
            //
            this.label17.AutoSize = true;
            this.label17.Location = new System.Drawing.Point(549, 435);
            this.label17.Name = "label17";
            this.label17.Size = new System.Drawing.Size(83, 15);
            this.label17.TabIndex = 38;
            this.label17.Text = "mA电流运行";
            //
            // bt_SetCurrentCom
            //
            this.bt_SetCurrentCom.Location = new System.Drawing.Point(638, 428);
            this.bt_SetCurrentCom.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.bt_SetCurrentCom.Name = "bt_SetCurrentCom";
            this.bt_SetCurrentCom.Size = new System.Drawing.Size(136, 30);
            this.bt_SetCurrentCom.TabIndex = 37;
            this.bt_SetCurrentCom.Text = "发送命令";
            this.bt_SetCurrentCom.UseVisualStyleBackColor = true;
            this.bt_SetCurrentCom.Click += new System.EventHandler(this.bt_SetCurrentCom_Click);
            //
            // label18
            //
            this.label18.AutoSize = true;
            this.label18.Location = new System.Drawing.Point(446, 435);
            this.label18.Name = "label18";
            this.label18.Size = new System.Drawing.Size(22, 15);
            this.label18.TabIndex = 35;
            this.label18.Text = "以";
            //
            // bt_OpenDocunment
            //
            this.bt_OpenDocunment.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.bt_OpenDocunment.Location = new System.Drawing.Point(594, 316);
            this.bt_OpenDocunment.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.bt_OpenDocunment.Name = "bt_OpenDocunment";
            this.bt_OpenDocunment.Size = new System.Drawing.Size(179, 30);
            this.bt_OpenDocunment.TabIndex = 39;
            this.bt_OpenDocunment.Text = "打开电机说明文档";
            this.bt_OpenDocunment.UseVisualStyleBackColor = true;
            this.bt_OpenDocunment.Click += new System.EventHandler(this.bt_OpenDocunment_Click);
            //
            // numID
            //
            this.numID.Location = new System.Drawing.Point(681, 15);
            this.numID.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.numID.Minimum = new decimal(new int[] {
            0,
            0,
            0,
            0});
            this.numID.Maximum = new decimal(new int[] {
            7,
            0,
            0,
            0});
            this.numID.Name = "numID";
            this.numID.Size = new System.Drawing.Size(91, 25);
            this.numID.TabIndex = 40;
            //
            // numLimitCurrent
            //
            this.numLimitCurrent.Location = new System.Drawing.Point(192, 353);
            this.numLimitCurrent.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.numLimitCurrent.Maximum = new decimal(new int[] {
            4500,
            0,
            0,
            0});
            this.numLimitCurrent.Name = "numLimitCurrent";
            this.numLimitCurrent.Size = new System.Drawing.Size(92, 25);
            this.numLimitCurrent.TabIndex = 41;
            //
            // num_Velocity
            //
            this.num_Velocity.DecimalPlaces = 2;
            this.num_Velocity.Increment = new decimal(new int[] {
            1,
            0,
            0,
            131072});
            this.num_Velocity.Location = new System.Drawing.Point(449, 397);
            this.num_Velocity.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.num_Velocity.Maximum = new decimal(new int[] {
            99999,
            0,
            0,
            0});
            this.num_Velocity.Minimum = new decimal(new int[] {
            99999,
            0,
            0,
            -2147483648});
            this.num_Velocity.Name = "num_Velocity";
            this.num_Velocity.Size = new System.Drawing.Size(75, 25);
            this.num_Velocity.TabIndex = 44;
            //
            // num_Current
            //
            this.num_Current.Location = new System.Drawing.Point(469, 428);
            this.num_Current.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.num_Current.Maximum = new decimal(new int[] {
            4500,
            0,
            0,
            0});
            this.num_Current.Minimum = new decimal(new int[] {
            4500,
            0,
            0,
            -2147483648});
            this.num_Current.Name = "num_Current";
            this.num_Current.Size = new System.Drawing.Size(75, 25);
            this.num_Current.TabIndex = 45;
            //
            // bt_Enable
            //
            this.bt_Enable.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.bt_Enable.Location = new System.Drawing.Point(594, 46);
            this.bt_Enable.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.bt_Enable.Name = "bt_Enable";
            this.bt_Enable.Size = new System.Drawing.Size(179, 30);
            this.bt_Enable.TabIndex = 46;
            this.bt_Enable.Text = "使能电机";
            this.bt_Enable.UseVisualStyleBackColor = true;
            this.bt_Enable.Click += new System.EventHandler(this.bt_Enable_Click);
            //
            // bt_StopNow
            //
            this.bt_StopNow.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.bt_StopNow.Location = new System.Drawing.Point(594, 214);
            this.bt_StopNow.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.bt_StopNow.Name = "bt_StopNow";
            this.bt_StopNow.Size = new System.Drawing.Size(179, 30);
            this.bt_StopNow.TabIndex = 48;
            this.bt_StopNow.Text = "立即停止";
            this.bt_StopNow.UseVisualStyleBackColor = true;
            this.bt_StopNow.Click += new System.EventHandler(this.bt_StopNow_Click);
            //
            // bt_ClearFault
            //
            this.bt_ClearFault.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.bt_ClearFault.Location = new System.Drawing.Point(594, 464);
            this.bt_ClearFault.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.bt_ClearFault.Name = "bt_ClearFault";
            this.bt_ClearFault.Size = new System.Drawing.Size(179, 30);
            this.bt_ClearFault.TabIndex = 93;
            this.bt_ClearFault.Text = "清除驱动故障";
            this.bt_ClearFault.UseVisualStyleBackColor = true;
            this.bt_ClearFault.Click += new System.EventHandler(this.bt_ClearFault_Click);
            //
            // num_Position
            //
            this.num_Position.DecimalPlaces = 3;
            this.num_Position.Increment = new decimal(new int[] {
            1,
            0,
            0,
            196608});
            this.num_Position.Location = new System.Drawing.Point(526, 366);
            this.num_Position.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.num_Position.Maximum = new decimal(new int[] {
            99999,
            0,
            0,
            0});
            this.num_Position.Minimum = new decimal(new int[] {
            99999,
            0,
            0,
            -2147483648});
            this.num_Position.Name = "num_Position";
            this.num_Position.Size = new System.Drawing.Size(75, 25);
            this.num_Position.TabIndex = 49;
            //
            // list_Postions
            //
            this.list_Postions.FormattingEnabled = true;
            this.list_Postions.ItemHeight = 15;
            this.list_Postions.Location = new System.Drawing.Point(364, 39);
            this.list_Postions.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.list_Postions.Name = "list_Postions";
            this.list_Postions.Size = new System.Drawing.Size(176, 139);
            this.list_Postions.TabIndex = 50;
            //
            // label4
            //
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(364, 18);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(75, 15);
            this.label4.TabIndex = 51;
            this.label4.Text = "位置列表:";
            //
            // bt_RecordPos
            //
            this.bt_RecordPos.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.bt_RecordPos.Location = new System.Drawing.Point(363, 188);
            this.bt_RecordPos.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.bt_RecordPos.Name = "bt_RecordPos";
            this.bt_RecordPos.Size = new System.Drawing.Size(177, 30);
            this.bt_RecordPos.TabIndex = 52;
            this.bt_RecordPos.Text = "记录当前位置";
            this.bt_RecordPos.UseVisualStyleBackColor = true;
            this.bt_RecordPos.Click += new System.EventHandler(this.bt_RecordPos_Click);
            //
            // bt_LoopPos
            //
            this.bt_LoopPos.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.bt_LoopPos.Location = new System.Drawing.Point(363, 222);
            this.bt_LoopPos.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.bt_LoopPos.Name = "bt_LoopPos";
            this.bt_LoopPos.Size = new System.Drawing.Size(177, 30);
            this.bt_LoopPos.TabIndex = 53;
            this.bt_LoopPos.Text = "循环运动";
            this.bt_LoopPos.UseVisualStyleBackColor = true;
            this.bt_LoopPos.Click += new System.EventHandler(this.bt_LoopPos_Click);
            //
            // lb_LimitVelocity
            //
            this.lb_LimitVelocity.AutoSize = true;
            this.lb_LimitVelocity.Location = new System.Drawing.Point(189, 200);
            this.lb_LimitVelocity.MinimumSize = new System.Drawing.Size(149, 0);
            this.lb_LimitVelocity.Name = "lb_LimitVelocity";
            this.lb_LimitVelocity.Size = new System.Drawing.Size(149, 15);
            this.lb_LimitVelocity.TabIndex = 55;
            this.lb_LimitVelocity.Text = "（空）";
            this.lb_LimitVelocity.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // label8
            //
            this.label8.AutoSize = true;
            this.label8.Location = new System.Drawing.Point(28, 200);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(168, 15);
            this.label8.TabIndex = 54;
            this.label8.Text = "电机限制速度(rev/s)：";
            //
            // num_LimitVelocity
            //
            this.num_LimitVelocity.Location = new System.Drawing.Point(192, 388);
            this.num_LimitVelocity.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.num_LimitVelocity.Maximum = new decimal(new int[] {
            3500,
            0,
            0,
            0});
            this.num_LimitVelocity.Name = "num_LimitVelocity";
            this.num_LimitVelocity.Size = new System.Drawing.Size(92, 25);
            this.num_LimitVelocity.TabIndex = 58;
            //
            // bt_SetLimitVelocity
            //
            this.bt_SetLimitVelocity.Location = new System.Drawing.Point(290, 386);
            this.bt_SetLimitVelocity.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.bt_SetLimitVelocity.Name = "bt_SetLimitVelocity";
            this.bt_SetLimitVelocity.Size = new System.Drawing.Size(100, 30);
            this.bt_SetLimitVelocity.TabIndex = 57;
            this.bt_SetLimitVelocity.Text = "设置";
            this.bt_SetLimitVelocity.UseVisualStyleBackColor = true;
            this.bt_SetLimitVelocity.Click += new System.EventHandler(this.bt_SetLimitVelocity_Click);
            //
            // label6
            //
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(29, 391);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(168, 15);
            this.label6.TabIndex = 56;
            this.label6.Text = "设置限制速度(rev/s)：";
            //
            // bt_SetThisIsHome
            //
            this.bt_SetThisIsHome.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.bt_SetThisIsHome.Location = new System.Drawing.Point(594, 248);
            this.bt_SetThisIsHome.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.bt_SetThisIsHome.Name = "bt_SetThisIsHome";
            this.bt_SetThisIsHome.Size = new System.Drawing.Size(179, 30);
            this.bt_SetThisIsHome.TabIndex = 64;
            this.bt_SetThisIsHome.Text = "设置当前为起始点";
            this.bt_SetThisIsHome.UseVisualStyleBackColor = true;
            this.bt_SetThisIsHome.Click += new System.EventHandler(this.bt_SetThisIsHome_Click);
            //
            // bt_SetKp
            //
            this.bt_SetKp.Location = new System.Drawing.Point(159, 242);
            this.bt_SetKp.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.bt_SetKp.Name = "bt_SetKp";
            this.bt_SetKp.Size = new System.Drawing.Size(83, 26);
            this.bt_SetKp.TabIndex = 74;
            this.bt_SetKp.Text = "设置";
            this.bt_SetKp.UseVisualStyleBackColor = true;
            this.bt_SetKp.Click += new System.EventHandler(this.bt_SetKp_Click);
            //
            // num_Kd
            //
            this.num_Kd.Location = new System.Drawing.Point(61, 323);
            this.num_Kd.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.num_Kd.Maximum = new decimal(new int[] {
            999999,
            0,
            0,
            0});
            this.num_Kd.Name = "num_Kd";
            this.num_Kd.Size = new System.Drawing.Size(92, 25);
            this.num_Kd.TabIndex = 78;
            //
            // num_Ki
            //
            this.num_Ki.Location = new System.Drawing.Point(61, 296);
            this.num_Ki.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.num_Ki.Maximum = new decimal(new int[] {
            999999,
            0,
            0,
            0});
            this.num_Ki.Name = "num_Ki";
            this.num_Ki.Size = new System.Drawing.Size(92, 25);
            this.num_Ki.TabIndex = 77;
            //
            // num_Kv
            //
            this.num_Kv.Location = new System.Drawing.Point(61, 269);
            this.num_Kv.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.num_Kv.Maximum = new decimal(new int[] {
            999999,
            0,
            0,
            0});
            this.num_Kv.Name = "num_Kv";
            this.num_Kv.Size = new System.Drawing.Size(92, 25);
            this.num_Kv.TabIndex = 76;
            //
            // num_Kp
            //
            this.num_Kp.Location = new System.Drawing.Point(61, 242);
            this.num_Kp.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.num_Kp.Maximum = new decimal(new int[] {
            999999,
            0,
            0,
            0});
            this.num_Kp.Name = "num_Kp";
            this.num_Kp.Size = new System.Drawing.Size(92, 25);
            this.num_Kp.TabIndex = 74;
            //
            // label11
            //
            this.label11.AutoSize = true;
            this.label11.Location = new System.Drawing.Point(29, 327);
            this.label11.Name = "label11";
            this.label11.Size = new System.Drawing.Size(38, 15);
            this.label11.TabIndex = 75;
            this.label11.Text = "Kd：";
            //
            // label19
            //
            this.label19.AutoSize = true;
            this.label19.Location = new System.Drawing.Point(29, 273);
            this.label19.Name = "label19";
            this.label19.Size = new System.Drawing.Size(38, 15);
            this.label19.TabIndex = 74;
            this.label19.Text = "Kv：";
            //
            // label21
            //
            this.label21.AutoSize = true;
            this.label21.Location = new System.Drawing.Point(29, 300);
            this.label21.Name = "label21";
            this.label21.Size = new System.Drawing.Size(38, 15);
            this.label21.TabIndex = 73;
            this.label21.Text = "Ki：";
            //
            // label23
            //
            this.label23.AutoSize = true;
            this.label23.Location = new System.Drawing.Point(29, 244);
            this.label23.Name = "label23";
            this.label23.Size = new System.Drawing.Size(38, 15);
            this.label23.TabIndex = 72;
            this.label23.Text = "Kp：";
            //
            // bt_ReSet
            //
            this.bt_ReSet.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.bt_ReSet.Location = new System.Drawing.Point(594, 282);
            this.bt_ReSet.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.bt_ReSet.Name = "bt_ReSet";
            this.bt_ReSet.Size = new System.Drawing.Size(179, 30);
            this.bt_ReSet.TabIndex = 74;
            this.bt_ReSet.Text = "恢复出厂默认值";
            this.bt_ReSet.UseVisualStyleBackColor = true;
            this.bt_ReSet.Click += new System.EventHandler(this.bt_ReSet_Click);
            //
            // bt_SetKv
            //
            this.bt_SetKv.Location = new System.Drawing.Point(159, 269);
            this.bt_SetKv.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.bt_SetKv.Name = "bt_SetKv";
            this.bt_SetKv.Size = new System.Drawing.Size(83, 26);
            this.bt_SetKv.TabIndex = 79;
            this.bt_SetKv.Text = "设置";
            this.bt_SetKv.UseVisualStyleBackColor = true;
            this.bt_SetKv.Click += new System.EventHandler(this.bt_SetKv_Click);
            //
            // bt_SetKi
            //
            this.bt_SetKi.Location = new System.Drawing.Point(159, 295);
            this.bt_SetKi.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.bt_SetKi.Name = "bt_SetKi";
            this.bt_SetKi.Size = new System.Drawing.Size(83, 26);
            this.bt_SetKi.TabIndex = 80;
            this.bt_SetKi.Text = "设置";
            this.bt_SetKi.UseVisualStyleBackColor = true;
            this.bt_SetKi.Click += new System.EventHandler(this.bt_SetKi_Click);
            //
            // bt_SetKd
            //
            this.bt_SetKd.Location = new System.Drawing.Point(159, 322);
            this.bt_SetKd.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.bt_SetKd.Name = "bt_SetKd";
            this.bt_SetKd.Size = new System.Drawing.Size(83, 26);
            this.bt_SetKd.TabIndex = 81;
            this.bt_SetKd.Text = "设置";
            this.bt_SetKd.UseVisualStyleBackColor = true;
            this.bt_SetKd.Click += new System.EventHandler(this.bt_SetKd_Click);
            //
            // label12
            //
            this.label12.AutoSize = true;
            this.label12.Location = new System.Drawing.Point(364, 291);
            this.label12.Name = "label12";
            this.label12.Size = new System.Drawing.Size(112, 15);
            this.label12.TabIndex = 83;
            this.label12.Text = "循环间隔时间：";
            //
            // num_LoopSleepTime
            //
            this.num_LoopSleepTime.Location = new System.Drawing.Point(367, 310);
            this.num_LoopSleepTime.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.num_LoopSleepTime.Maximum = new decimal(new int[] {
            999999,
            0,
            0,
            0});
            this.num_LoopSleepTime.Name = "num_LoopSleepTime";
            this.num_LoopSleepTime.Size = new System.Drawing.Size(144, 25);
            this.num_LoopSleepTime.TabIndex = 84;
            this.num_LoopSleepTime.Value = new decimal(new int[] {
            1000,
            0,
            0,
            0});
            //
            // label20
            //
            this.label20.AutoSize = true;
            this.label20.Location = new System.Drawing.Point(521, 316);
            this.label20.Name = "label20";
            this.label20.Size = new System.Drawing.Size(23, 15);
            this.label20.TabIndex = 85;
            this.label20.Text = "ms";
            //
            // num_ACurrent
            //
            this.num_ACurrent.Location = new System.Drawing.Point(192, 425);
            this.num_ACurrent.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.num_ACurrent.Maximum = new decimal(new int[] {
            3500,
            0,
            0,
            0});
            this.num_ACurrent.Name = "num_ACurrent";
            this.num_ACurrent.Size = new System.Drawing.Size(92, 25);
            this.num_ACurrent.TabIndex = 87;
            //
            // bt_ACurrent
            //
            this.bt_ACurrent.Location = new System.Drawing.Point(290, 423);
            this.bt_ACurrent.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.bt_ACurrent.Name = "bt_ACurrent";
            this.bt_ACurrent.Size = new System.Drawing.Size(100, 30);
            this.bt_ACurrent.TabIndex = 86;
            this.bt_ACurrent.Text = "设置";
            this.bt_ACurrent.UseVisualStyleBackColor = true;
            this.bt_ACurrent.Click += new System.EventHandler(this.bt_ACurrent_Click);
            //
            // num_BCurrent
            //
            this.num_BCurrent.Location = new System.Drawing.Point(192, 459);
            this.num_BCurrent.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.num_BCurrent.Maximum = new decimal(new int[] {
            3500,
            0,
            0,
            0});
            this.num_BCurrent.Name = "num_BCurrent";
            this.num_BCurrent.Size = new System.Drawing.Size(92, 25);
            this.num_BCurrent.TabIndex = 89;
            //
            // bt_BCurrent
            //
            this.bt_BCurrent.Location = new System.Drawing.Point(290, 457);
            this.bt_BCurrent.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.bt_BCurrent.Name = "bt_BCurrent";
            this.bt_BCurrent.Size = new System.Drawing.Size(100, 30);
            this.bt_BCurrent.TabIndex = 88;
            this.bt_BCurrent.Text = "设置";
            this.bt_BCurrent.UseVisualStyleBackColor = true;
            this.bt_BCurrent.Click += new System.EventHandler(this.bt_BCurrent_Click);
            //
            // label22
            //
            this.label22.AutoSize = true;
            this.label22.Location = new System.Drawing.Point(29, 427);
            this.label22.Name = "label22";
            this.label22.Size = new System.Drawing.Size(114, 15);
            this.label22.TabIndex = 90;
            this.label22.Text = "A相电流（mA）:";
            //
            // label24
            //
            this.label24.AutoSize = true;
            this.label24.Location = new System.Drawing.Point(29, 461);
            this.label24.Name = "label24";
            this.label24.Size = new System.Drawing.Size(114, 15);
            this.label24.TabIndex = 91;
            this.label24.Text = "B相电流（mA）:";
            //
            // bt_DeleteLast
            //
            this.bt_DeleteLast.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.bt_DeleteLast.Location = new System.Drawing.Point(363, 256);
            this.bt_DeleteLast.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.bt_DeleteLast.Name = "bt_DeleteLast";
            this.bt_DeleteLast.Size = new System.Drawing.Size(177, 30);
            this.bt_DeleteLast.TabIndex = 82;
            this.bt_DeleteLast.Text = "删除最后一个";
            this.bt_DeleteLast.UseVisualStyleBackColor = true;
            this.bt_DeleteLast.Click += new System.EventHandler(this.bt_DeleteLast_Click);
            //
            // lb_LimitStatus
            //
            this.lb_LimitStatus.AutoSize = true;
            this.lb_LimitStatus.Location = new System.Drawing.Point(189, 218);
            this.lb_LimitStatus.MinimumSize = new System.Drawing.Size(149, 0);
            this.lb_LimitStatus.Name = "lb_LimitStatus";
            this.lb_LimitStatus.Size = new System.Drawing.Size(149, 15);
            this.lb_LimitStatus.TabIndex = 84;
            this.lb_LimitStatus.Text = "未触发";
            this.lb_LimitStatus.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // label25
            //
            this.label25 = new System.Windows.Forms.Label();
            this.label25.AutoSize = true;
            this.label25.Location = new System.Drawing.Point(28, 218);
            this.label25.Name = "label25";
            this.label25.Size = new System.Drawing.Size(114, 15);
            this.label25.TabIndex = 85;
            this.label25.Text = "限位状态：";
            //
            // Form1
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(799, 497);
            this.Controls.Add(this.label24);
            this.Controls.Add(this.label22);
            this.Controls.Add(this.num_BCurrent);
            this.Controls.Add(this.bt_BCurrent);
            this.Controls.Add(this.num_ACurrent);
            this.Controls.Add(this.bt_ACurrent);
            this.Controls.Add(this.label20);
            this.Controls.Add(this.num_LoopSleepTime);
            this.Controls.Add(this.label12);
            this.Controls.Add(this.bt_DeleteLast);
            this.Controls.Add(this.bt_SetKd);
            this.Controls.Add(this.bt_SetKi);
            this.Controls.Add(this.bt_SetKv);
            this.Controls.Add(this.num_Kd);
            this.Controls.Add(this.num_Ki);
            this.Controls.Add(this.bt_SetKp);
            this.Controls.Add(this.num_Kv);
            this.Controls.Add(this.bt_ReSet);
            this.Controls.Add(this.num_Kp);
            this.Controls.Add(this.label11);
            this.Controls.Add(this.label19);
            this.Controls.Add(this.label21);
            this.Controls.Add(this.label23);
            this.Controls.Add(this.bt_SetThisIsHome);
            this.Controls.Add(this.num_LimitVelocity);
            this.Controls.Add(this.bt_SetLimitVelocity);
            this.Controls.Add(this.label6);
            this.Controls.Add(this.lb_LimitVelocity);
            this.Controls.Add(this.label8);
            this.Controls.Add(this.lb_LimitStatus);
            this.Controls.Add(this.label25);
            this.Controls.Add(this.bt_LoopPos);
            this.Controls.Add(this.bt_RecordPos);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.list_Postions);
            this.Controls.Add(this.num_Position);
            this.Controls.Add(this.bt_StopNow);
            this.Controls.Add(this.bt_ClearFault);
            this.Controls.Add(this.bt_Enable);
            this.Controls.Add(this.num_Current);
            this.Controls.Add(this.num_Velocity);
            this.Controls.Add(this.numLimitCurrent);
            this.Controls.Add(this.numID);
            this.Controls.Add(this.bt_OpenDocunment);
            this.Controls.Add(this.label17);
            this.Controls.Add(this.bt_SetCurrentCom);
            this.Controls.Add(this.label18);
            this.Controls.Add(this.label15);
            this.Controls.Add(this.bt_SetVelocityCom);
            this.Controls.Add(this.label16);
            this.Controls.Add(this.bt_Call);
            this.Controls.Add(this.label13);
            this.Controls.Add(this.bt_SetPosCom);
            this.Controls.Add(this.label14);
            this.Controls.Add(this.bt_SetLimitCurrent);
            this.Controls.Add(this.label10);
            this.Controls.Add(this.lb_LimitCurrent);
            this.Controls.Add(this.label9);
            this.Controls.Add(this.bt_InOnce);
            this.Controls.Add(this.bt_SpeedOnce);
            this.Controls.Add(this.bt_PosOnce);
            this.Controls.Add(this.lb_Current);
            this.Controls.Add(this.label7);
            this.Controls.Add(this.lb_Velocity);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.lb_Pos);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.bt_ReadInfo);
            this.Controls.Add(this.lb_ID);
            this.Controls.Add(this.label1111);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.cb_ComList);
            this.Controls.Add(this.bt_Link);
            this.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.Name = "Form1";
            this.Text = "TiAmo上位机";
            this.Load += new System.EventHandler(this.Form1_Load);
            ((System.ComponentModel.ISupportInitialize)(this.numID)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numLimitCurrent)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.num_Velocity)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.num_Current)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.num_Position)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.num_LimitVelocity)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.num_Kd)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.num_Ki)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.num_Kv)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.num_Kp)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.num_LoopSleepTime)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.num_ACurrent)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.num_BCurrent)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button bt_Link;
        private System.Windows.Forms.ComboBox cb_ComList;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label1111;
        private System.Windows.Forms.Label lb_ID;
        private System.Windows.Forms.Button bt_ReadInfo;
        private System.Windows.Forms.Label lb_Pos;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label lb_Velocity;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.Label lb_Current;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.Button bt_PosOnce;
        private System.Windows.Forms.Button bt_SpeedOnce;
        private System.Windows.Forms.Button bt_InOnce;
        private System.Windows.Forms.Label lb_LimitCurrent;
        private System.Windows.Forms.Label label9;
        private System.Windows.Forms.Label label10;
        private System.Windows.Forms.Button bt_SetLimitCurrent;
        private System.Windows.Forms.Label label13;
        private System.Windows.Forms.Button bt_SetPosCom;
        private System.Windows.Forms.Label label14;
        private System.Windows.Forms.Button bt_Call;
        private System.Windows.Forms.Label label15;
        private System.Windows.Forms.Button bt_SetVelocityCom;
        private System.Windows.Forms.Label label16;
        private System.Windows.Forms.Label label17;
        private System.Windows.Forms.Button bt_SetCurrentCom;
        private System.Windows.Forms.Label label18;
        private System.Windows.Forms.Button bt_OpenDocunment;
        private System.Windows.Forms.NumericUpDown numID;
        private System.Windows.Forms.NumericUpDown numLimitCurrent;
        private System.Windows.Forms.NumericUpDown num_Velocity;
        private System.Windows.Forms.NumericUpDown num_Current;
        private System.Windows.Forms.Button bt_Enable;
        private System.Windows.Forms.Button bt_StopNow;
        private System.Windows.Forms.Button bt_ClearFault;
        private System.Windows.Forms.NumericUpDown num_Position;
        private System.Windows.Forms.ListBox list_Postions;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Button bt_RecordPos;
        private System.Windows.Forms.Button bt_LoopPos;
        private System.Windows.Forms.Label lb_LimitVelocity;
        private System.Windows.Forms.Label label8;
        private System.Windows.Forms.NumericUpDown num_LimitVelocity;
        private System.Windows.Forms.Button bt_SetLimitVelocity;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.Button bt_SetThisIsHome;
        private System.Windows.Forms.Button bt_SetKp;
        private System.Windows.Forms.NumericUpDown num_Kd;
        private System.Windows.Forms.NumericUpDown num_Ki;
        private System.Windows.Forms.NumericUpDown num_Kv;
        private System.Windows.Forms.NumericUpDown num_Kp;
        private System.Windows.Forms.Label label11;
        private System.Windows.Forms.Label label19;
        private System.Windows.Forms.Label label21;
        private System.Windows.Forms.Label label23;
        private System.Windows.Forms.Button bt_ReSet;
        private System.Windows.Forms.Button bt_SetKv;
        private System.Windows.Forms.Button bt_SetKi;
        private System.Windows.Forms.Button bt_SetKd;
        private System.Windows.Forms.Label label12;
        private System.Windows.Forms.NumericUpDown num_LoopSleepTime;
        private System.Windows.Forms.Label label20;
        private System.Windows.Forms.NumericUpDown num_ACurrent;
        private System.Windows.Forms.Button bt_ACurrent;
        private System.Windows.Forms.NumericUpDown num_BCurrent;
        private System.Windows.Forms.Button bt_BCurrent;
        private System.Windows.Forms.Label label22;
        private System.Windows.Forms.Label label24;
        private System.Windows.Forms.Button bt_DeleteLast;
        private System.Windows.Forms.Label lb_LimitStatus;
        private System.Windows.Forms.Label label25;
    }
}
