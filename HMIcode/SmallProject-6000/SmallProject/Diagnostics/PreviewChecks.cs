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
            var validationTimings=new System.Collections.Generic.List<object>();
            void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); passed++; }
            try
            {
                var preview = window.JykerPreviewContrl;
                await preview.WaitUntilReadyAsync();
                Check(!App.Core.Serial.IsOpen, "Serial must stay disconnected");
                var zero = new double[6];
                var testArm=new SmallProject.Devices.Arm.JykerArm();
                var expectedDirections=new[]{-1,1,-1,1,-1,-1};
                for(int axis=0;axis<6;axis++) {
                    var motor=testArm.motorJ[axis];
                    Check(motor.Direction==expectedDirections[axis],$"J{axis+1} uses the reversed harness direction");
                    foreach(double target in new[]{1.0,-1.0,0.0}) {
                        float revolutions=(float)(target*motor.Direction*motor.Reduction/360);
                        testArm.RecievePos(axis+1,revolutions,true);
                        Check(Math.Abs(motor.Angle-target)<1e-6,$"J{axis+1} signed feedback round trip {target}");
                    }
                }
                var directionTarget=new double[]{.5,-.5,.5,.5,.5,30};
                var directionPlan=ContinuousPathPlanner.Build(testArm.dof6Solver.Model,zero,new[]{directionTarget},
                    new[]{2.0},testArm.motorJ.Select(m=>m.Direction*m.Reduction/360).ToArray(),false);
                Check((await preview.ValidatePathAsync(directionPlan.Samples)).ok,"Reversed motor signs preserve the model collision path");
                var queried=new System.Collections.Generic.List<int>();
                var singlePosition=await testArm.ReadPositionsAsync(node=>{
                    queried.Add(node); testArm.RecievePos(node,.25f,true);
                },()=>true,100,new[]{2});
                double expectedJ3=.25*360/(testArm.motorJ[2].Direction*testArm.motorJ[2].Reduction);
                Check(queried.SequenceEqual(new[]{3}) && singlePosition!=null && Math.Abs(singlePosition[2]-expectedJ3)<1e-6,
                    "J3-only feedback succeeds without the other five boards and converts motor revolutions");
                Check(singlePosition!.Where((v,i)=>i!=2).All(v=>v==0),"Absent axes retain the last model position");
                var nextPosition=await testArm.ReadPositionsAsync(node=>testArm.RecievePos(node,.5f,true),()=>true,100,new[]{2});
                Check(nextPosition!=null && Math.Abs(nextPosition[2]-2*expectedJ3)<1e-6,
                    "The next single-axis move reads a new encoder position");
                Check(await testArm.ReadPositionsAsync(_=>{},()=>true,60,new[]{2})==null,
                    "Stale J3 feedback cannot start a new move");
                Check(await testArm.ReadPositionsAsync(_=>testArm.RecievePos(2,0,true),()=>true,60,new[]{2})==null,
                    "Another node cannot satisfy the J3 feedback wait");
                Check(await testArm.ReadPositionsAsync(node=>{if(node==3)testArm.RecievePos(3,0,true);},()=>true,60)==null,
                    "Six-axis mode still requires all six fresh encoder responses");
                using(var cancelled=new System.Threading.CancellationTokenSource()) {
                    cancelled.Cancel();
                    bool wasCancelled=false;
                    try { await testArm.ReadPositionsAsync(_=>{},()=>true,100,new[]{2},cancelled.Token); }
                    catch(OperationCanceledException) { wasCancelled=true; }
                    Check(wasCancelled,"Single-axis encoder wait observes stop cancellation");
                }
                var plan=ContinuousPathPlanner.Build(App.Core.Jyker.dof6Solver.Model,zero,
                    new[]{new double[]{0,-.5,.5,0,0,0},new double[]{0,-.5,-.5,0,0,0},new double[6]},
                    new[]{1.0,1.0,1.0},new[]{testArm.motorJ[1].Direction*testArm.motorJ[1].Reduction/360,
                        testArm.motorJ[2].Direction*testArm.motorJ[2].Reduction/360},false,jointIndices:new[]{1,2});
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
                    new[]{1.0,1.0,1.0},testArm.motorJ.Select(m=>m.Direction*m.Reduction/360).ToArray(),false);
                Check((await preview.ValidatePathAsync(fivePlan.Samples)).ok,"Six-axis rounded wire curve passes WebView validation");
                foreach(double angle in new[]{10.0,150.0}) {
                    var target=new double[]{0,0,angle,0,0,0};
                    var watch=System.Diagnostics.Stopwatch.StartNew();
                    var single=ContinuousPathPlanner.Build(App.Core.Jyker.dof6Solver.Model,zero,new[]{target},
                        new[]{30.0},new[]{testArm.motorJ[2].Direction*testArm.motorJ[2].Reduction/360},false,jointIndices:new[]{2});
                    long planned=watch.ElapsedMilliseconds;
                    foreach(var samples in single.ValidationPaths())
                        Check((await preview.ValidatePathAsync(samples)).ok,$"Actual WebView accepts the J3 {angle} degree path");
                    validationTimings.Add(new{angle,samples=single.Samples.Count,planningMs=planned,collisionMs=watch.ElapsedMilliseconds-planned});
                }
                Check(window.cb_SingleAxis.SelectedIndex==2 && !window.bt_MoveSingleAxis.IsEnabled,
                    "Single-axis selector defaults to J3 and motion is disabled while disconnected");
                string savedJ1=window.tb_Joint1.Text,savedJ3=window.tb_Joint3.Text;
                window.tb_Joint1.Text="invalid"; window.tb_Joint3.Text="1";
                Check(window.JykerKinematicContrl.TryReadModelJoint(2,out double singleTarget) && singleTarget==1,
                    "Single-axis input ignores unrelated joint target fields");
                window.tb_Joint3.Text="9999";
                Check(!window.JykerKinematicContrl.TryReadModelJoint(2,out _),"Single-axis target outside URDF limits is rejected");
                window.tb_Joint1.Text=savedJ1; window.tb_Joint3.Text=savedJ3;
                // Exercise scrolling with less space than the operation controls need.
                window.OperationScroll.Height=240;
                window.UpdateLayout();
                Check(window.OperationScroll.ViewportHeight>0 && window.OperationScroll.ScrollableHeight>0,
                    "Operation panel scrolls in a short viewport");
                window.OperationScroll.ScrollToBottom();
                await window.Dispatcher.InvokeAsync(()=>window.UpdateLayout(),System.Windows.Threading.DispatcherPriority.ContextIdle);
                window.bt_StartDetect.IsEnabled=true;
                var videoPoint=window.bt_StartDetect.TranslatePoint(new System.Windows.Point(
                    window.bt_StartDetect.ActualWidth/2,window.bt_StartDetect.ActualHeight/2),window);
                var videoHit=window.InputHitTest(videoPoint) as System.Windows.DependencyObject;
                Check(videoHit==window.bt_StartDetect || (videoHit!=null && window.bt_StartDetect.IsAncestorOf(videoHit)),
                    "Bottom video button is reachable after scrolling");
                window.bt_StartDetect.IsEnabled=false;
                window.bt_MoveSingleAxis.IsEnabled=true;
                var singlePoint=window.bt_MoveSingleAxis.TranslatePoint(new System.Windows.Point(
                    window.bt_MoveSingleAxis.ActualWidth/2,window.bt_MoveSingleAxis.ActualHeight/2),window);
                var singleHit=window.InputHitTest(singlePoint) as System.Windows.DependencyObject;
                Check(singleHit==window.bt_MoveSingleAxis || (singleHit!=null && window.bt_MoveSingleAxis.IsAncestorOf(singleHit)),
                    "Single-axis move button is reachable after scrolling");
                window.bt_MoveSingleAxis.IsEnabled=false;
                window.bt_MoveFivePath.BringIntoView();
                await window.Dispatcher.InvokeAsync(()=>window.UpdateLayout(),System.Windows.Threading.DispatcherPriority.ContextIdle);
                Check(window.bt_MoveFivePath.IsVisible && window.bt_MoveFivePath.ActualHeight>0,"Six-axis path button is visible");
                // A rendered button must also be reachable, not covered by another row.
                window.bt_MoveFivePath.IsEnabled=true;
                var buttonPoint=window.bt_MoveFivePath.TranslatePoint(new System.Windows.Point(
                    window.bt_MoveFivePath.ActualWidth/2,window.bt_MoveFivePath.ActualHeight/2),window);
                var hit=window.InputHitTest(buttonPoint) as System.Windows.DependencyObject;
                Check(hit==window.bt_MoveFivePath || (hit!=null && window.bt_MoveFivePath.IsAncestorOf(hit)),
                    "Six-axis path button is not covered by video controls");
                window.bt_MoveFivePath.IsEnabled=false;
                window.OperationScroll.ScrollToTop();
                await window.Dispatcher.InvokeAsync(()=>window.UpdateLayout(),System.Windows.Threading.DispatcherPriority.ContextIdle);
                var inputPoint=window.tb_X.TranslatePoint(new System.Windows.Point(window.tb_X.ActualWidth/2,window.tb_X.ActualHeight/2),window);
                var inputHit=window.InputHitTest(inputPoint) as System.Windows.DependencyObject;
                Check(inputHit==window.tb_X || (inputHit!=null && window.tb_X.IsAncestorOf(inputHit)),
                    "Top pose input is reachable after scrolling back");
                window.OperationScroll.Height=double.NaN;
                window.UpdateLayout();
                var torqueOrigin=window.gb_ArmControl.TranslatePoint(new System.Windows.Point(),window);
                var logOrigin=window.gb_Log.TranslatePoint(new System.Windows.Point(),window);
                Check(logOrigin.X>=torqueOrigin.X+window.gb_ArmControl.ActualWidth && Math.Abs(logOrigin.Y-torqueOrigin.Y)<1,
                    "Log panel sits beside the torque panel");
                Check(window.tbLog.IsEnabled && window.tbLog.IsReadOnly,
                    "Log remains selectable while torque monitoring is disabled");
                var savedLog=window.tbLog.Text;
                window.tbLog.Text=string.Join(Environment.NewLine,Enumerable.Range(1,100).Select(i=>$"日志滚动检查 {i}"));
                window.UpdateLayout();
                window.tbLog.ScrollToEnd();
                await window.Dispatcher.InvokeAsync(()=>window.UpdateLayout(),System.Windows.Threading.DispatcherPriority.ContextIdle);
                Check(window.tbLog.ExtentHeight>window.tbLog.ViewportHeight && window.tbLog.VerticalOffset>0,
                    "Long logs scroll to the latest entry");
                window.tbLog.ScrollToHome();
                await window.Dispatcher.InvokeAsync(()=>window.UpdateLayout(),System.Windows.Threading.DispatcherPriority.ContextIdle);
                Check(window.tbLog.VerticalOffset==0,"Log can scroll back to earlier entries");
                window.tbLog.Text=savedLog;
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
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"model-check-result.json"),JsonSerializer.Serialize(new {passed,error,validationTimings},new JsonSerializerOptions {WriteIndented=true}));
            window.RobotPreview.Dispose();
            System.Windows.Application.Current.Shutdown(error==null?0:1);
        }
    }
}
