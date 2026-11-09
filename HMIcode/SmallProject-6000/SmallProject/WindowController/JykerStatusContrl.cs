using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace SmallProject.WindowController
{
    public class JykerStatusContrl
    {
        private MainWindow M;
        private bool monitoring;
        private int refreshPending;

        public JykerStatusContrl(MainWindow m)
        {
            M = m;
            m.bt_ArmLoopStart.Click += Bt_ArmLoopStart_Click;
            m.bt_ArmLoopEnd.Click += Bt_ArmLoopEnd_Click;
        }

       

        //监听机械臂各个轴的状态
        private void Bt_ArmLoopStart_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (monitoring) return;
            monitoring = true;
            App.Core.Loop += LoopStatus;
            App.Core.Loop += App.Core.Jyker.LoopStatus;
            M.bt_ArmLoopStart.IsEnabled = false;
            M.bt_ArmLoopEnd.IsEnabled = true;
        }
        //结束监听机械臂各个轴的状态
        private void Bt_ArmLoopEnd_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            monitoring = false;
            App.Core.Loop -= LoopStatus;
            App.Core.Loop -= App.Core.Jyker.LoopStatus;
            M.bt_ArmLoopStart.IsEnabled = true;
            M.bt_ArmLoopEnd.IsEnabled = false;
        }

        //循环获取电机信息
        public void LoopStatus()
        {
            // Core already invokes this every 20 ms; queue at most one refresh.
            if (Interlocked.Exchange(ref refreshPending, 1) != 0) return;
            if (M.Dispatcher.HasShutdownStarted)
            {
                Interlocked.Exchange(ref refreshPending, 0);
                return;
            }
            M.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (!monitoring) return;
                    for (int i = 0; i < 6; i++)
                    {
                        double milliamps = App.Core.Jyker.motorJ[i].Current * 1000;
                        if (M.FindName($"pb_pJ{i + 1}") is ProgressBar pb) pb.Value = milliamps;
                        if (M.FindName($"tb_pJ{i + 1}") is TextBlock tb) tb.Text = milliamps.ToString("0");
                    }
                }
                finally { Interlocked.Exchange(ref refreshPending, 0); }
            }));
        }
    }
}
