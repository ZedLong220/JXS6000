using SmallProject.Configs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace SmallProject.Dialogs
{
    /// <summary>
    /// ConfigDialog.xaml 的交互逻辑
    /// </summary>
    public partial class ConfigDialog : Rubyer.RubyerWindow
    {
        public ConfigDialog()
        {
            InitializeComponent();
            //加载配置项
            this.DataContext = Newtonsoft.Json.JsonConvert.DeserializeObject<JConfiguration>(
                Newtonsoft.Json.JsonConvert.SerializeObject(App.JConfig));
            this.Loaded += ConfigDialog_Loaded;
        }

        //加载
        private void ConfigDialog_Loaded(object sender, RoutedEventArgs e)
        {
        }

        private void EnterOK_Click(object sender, RoutedEventArgs e)
        {
            if(App.Core.Jyker.IsPathBusy || App.Core.Serial.ControllerState!=0 ||
                (Application.Current.MainWindow is MainWindow main && main.JykerMoveContrl?.IsBusy==true))
            {
                Rubyer.MessageBoxR.Warning("请先停止运动或记录，再应用参数。");
                return;
            }
            var conf = (JConfiguration)DataContext;
            if(!conf.TryValidateMotion(out string error)) { Rubyer.MessageBoxR.Warning(error); return; }
            try { ConfigResposity.WriteConfigs(conf); }
            catch(Exception ex) { Rubyer.MessageBoxR.Warning("保存失败："+ex.Message); return; }
            App.JConfig=conf;
            Rubyer.MessageBoxR.Success("保存成功，运动参数将在下一次运动时生效；减速比变更需重启。");
        }
    }
}
