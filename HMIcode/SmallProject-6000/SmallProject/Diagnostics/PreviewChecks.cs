using SmallProject.Devices.Arm.Kinematic;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace SmallProject.Diagnostics
{
    // Opt-in integration check in the actual WPF/WebView2 host. No serial or
    // live-stream connection is started and no motor command is sent.
    internal static class PreviewChecks
    {
        public static async Task RunAsync(MainWindow window)
        {
            int passed = 0;
            string? error = null;
            void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); passed++; }
            try
            {
                var preview = window.JykerPreviewContrl;
                await preview.WaitUntilReadyAsync();
                Check(!App.Core.Serial.IsOpen, "Serial must stay disconnected");
                var zero = new double[6];
                var plan=ContinuousPathPlanner.Build(App.Core.Jyker.dof6Solver.Model,zero,
                    new[]{new double[]{0,-.5,.5,0,0,0},new double[]{0,-.5,-.5,0,0,0},new double[6]},
                    new[]{1.0,1.0,1.0},new[]{-150.0/360,150.0/360},false,jointIndices:new[]{1,2});
                Check((await preview.ValidatePathAsync(plan.Samples)).ok,"Actual rounded path passes native/WebView batch validation");
                Check(!(await preview.ValidatePathAsync(new[]{zero,new double[]{0,40,-40,0,0,0},zero})).ok,
                    "Old fixed test route is blocked by the new JXS6000 collision model");
                Check((await preview.ValidateMotionAsync(zero, new double[] {0,0,0,0,360,0})).ok, "J5 +360 path");
                Check((await preview.ValidateMotionAsync(zero, new double[] {0,0,0,0,-90,0})).ok, "J5 -90 path");
                Check((await preview.ValidateMotionAsync(zero, new double[] {0,0,0,-50,0,0})).ok, "J4 -50 path");
                Check((await preview.ValidateMotionAsync(zero, new double[] {0,0,0,140,0,0})).ok, "J4 +140 path");
                Check(!(await preview.ValidateMotionAsync(zero, new double[] {0,0,0,-60,0,0})).ok, "J4 obstruction");
                var expanded = new double[] {0,150,-150,0,0,0};
                preview.SetJoints(expanded);
                for (int attempt=0;attempt<40;attempt++)
                {
                    if (JointCoordinateMap.ToModel(App.Core.Jyker.prepareJoints.a).Zip(expanded).All(p=>Math.Abs(p.First-p.Second)<1e-8)) break;
                    await Task.Delay(50);
                }
                Check(JointCoordinateMap.ToModel(App.Core.Jyker.prepareJoints.a).Zip(expanded).All(p=>Math.Abs(p.First-p.Second)<1e-8), "Expanded J2/J3 angles synchronize through WebView");
                var fivePlan=ContinuousPathPlanner.Build(App.Core.Jyker.dof6Solver.Model,zero,
                    new[]{new double[]{.5,-.5,.5,.5,.5,30},new double[]{-.5,-.5,-.5,-.5,-.5,-45},zero},
                    new[]{1.0,1.0,1.0},new[]{30.0/360,-150.0/360,150.0/360,-100.0/360,100.0/360,1.0/360},false);
                Check((await preview.ValidatePathAsync(fivePlan.Samples)).ok,"Six-axis rounded wire curve passes WebView validation");
                Check(window.bt_MoveFivePath.IsVisible && window.bt_MoveFivePath.ActualHeight>0,"Six-axis path button is visible");
                // A rendered button must also be reachable, not covered by another row.
                window.bt_MoveFivePath.IsEnabled=true;
                var buttonPoint=window.bt_MoveFivePath.TranslatePoint(new System.Windows.Point(
                    window.bt_MoveFivePath.ActualWidth/2,window.bt_MoveFivePath.ActualHeight/2),window);
                var hit=window.InputHitTest(buttonPoint) as System.Windows.DependencyObject;
                Check(hit==window.bt_MoveFivePath || (hit!=null && window.bt_MoveFivePath.IsAncestorOf(hit)),
                    "Six-axis path button is not covered by video controls");
                window.bt_MoveFivePath.IsEnabled=false;
                Check(!window.tb_Joint6.IsReadOnly,"J6 input is editable");
                Check(window.pg_ClawAngle.Maximum==180 && window.bt_ClawApply.IsVisible,"Gripper PWM controls are available");
                Check(window.JykerKinematicContrl.ApplyModelJoints(new double[]{0,0,0,0,0,1}),"Native interface accepts physical J6 movement");
                Check(!window.JykerKinematicContrl.ApplyModelJoints(new double[]{0,0,0,0,0,181}),"J6 over +180 is rejected");
                Check((await preview.ValidateMotionAsync(new double[]{0,0,0,0,0,-180},new double[]{0,0,0,0,0,180})).ok,"J6 traverses the full -180 to +180 range");
                var q = new double[] {0,0,0,0,30,25};
                window.JykerKinematicContrl.ApplyModelJoints(q);
                Check((await preview.ValidateMotionAsync(zero, q)).ok, "Native-to-preview pose path");
                using var state = JsonDocument.Parse(await preview.ReadDiagnosticStateAsync());
                var root = state.RootElement;
                var actual = root.GetProperty("joints").EnumerateArray().Select(v => v.GetDouble()).ToArray();
                Check(actual.Zip(q).All(p => Math.Abs(p.First-p.Second)<1e-8), "Native-to-WebView joint synchronization");
                Check(root.GetProperty("colliders").GetInt32()==6 && root.GetProperty("hidden").GetBoolean(), "Six hidden collision models loaded");
                Check(root.GetProperty("width").GetInt32()>0 && root.GetProperty("height").GetInt32()>0, "Nonzero canvas size");
                var tool = root.GetProperty("tool").EnumerateArray().Select(v=>v.GetDouble()).ToArray();
                var expected = App.Core.Jyker.dof6Solver.Model.ToolTransform(q);
                Check(Enumerable.Range(0,16).All(i=>Math.Abs(tool[(i%4)*4+i/4]-expected[i])<1e-8), "Native FK equals WebView tool0 transform");
                Check(JointCoordinateMap.ToModel(App.Core.Jyker.prepareJoints.a).Zip(q).All(p=>Math.Abs(p.First-p.Second)<1e-8), "Preview-to-native message synchronization");
                var reverse = new double[] {0,0,0,0,-20,-15};
                preview.SetJoints(reverse);
                for (int attempt=0;attempt<40;attempt++)
                {
                    if (JointCoordinateMap.ToModel(App.Core.Jyker.prepareJoints.a).Zip(reverse).All(p=>Math.Abs(p.First-p.Second)<1e-8)) break;
                    await Task.Delay(50);
                }
                Check(JointCoordinateMap.ToModel(App.Core.Jyker.prepareJoints.a).Zip(reverse).All(p=>Math.Abs(p.First-p.Second)<1e-8), "WebView-to-native changed angles");
                Check(JointCoordinateMap.ToModel(App.Core.Jyker.currentJoints.a).All(v=>v==0), "Preview did not change last motor command");
            }
            catch (Exception ex) { error = ex.ToString(); }
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"model-check-result.json"),JsonSerializer.Serialize(new {passed,error},new JsonSerializerOptions {WriteIndented=true}));
            window.RobotPreview.Dispose();
            System.Windows.Application.Current.Shutdown(error==null?0:1);
        }
    }
}
