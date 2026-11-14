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
                Check(window.JykerKinematicContrl.TryReadPathTolerance(out double defaultTolerance) && defaultTolerance==20,
                    "Recorded paths default to the visible 20 mm smoothing allowance");
                foreach(string invalid in new[]{"NaN","0","51","invalid"}) {
                    window.tb_PathTolerance.Text=invalid;
                    Check(!window.JykerKinematicContrl.TryReadPathTolerance(out _),"Invalid smoothing allowance is rejected");
                }
                var recordedLoop=new[]{new double[]{-10,-10,-10,0,0,0},new double[]{10,10,-30,0,0,0},zero,zero};
                foreach(double tolerance in new[]{1.0,20,50}) {
                    window.tb_PathTolerance.Text=tolerance.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    Check(window.JykerKinematicContrl.TryReadPathTolerance(out double entered) && entered==tolerance,
                        "Smoothing input accepts the supported precision range");
                    var smoothLoop=ContinuousPathPlanner.Build(testArm.dof6Solver.Model,zero,recordedLoop,new[]{2.0,3,4,.05},
                        testArm.motorJ.Take(3).Select(m=>m.Direction*m.Reduction/360).ToArray(),true,
                        toleranceMm:tolerance,jointIndices:new[]{0,1,2});
                    foreach(var curve in smoothLoop.ValidationPaths())
                        Check((await preview.ValidatePathAsync(curve)).ok,$"Recorded three-point loop passes actual collision checks at {tolerance} mm");
                }
                window.tb_PathTolerance.Text="20";
                Check(App.JConfig.TryValidateMotion(out _),"Saved motion settings are valid");
                Check(testArm.motorJ.Select(m=>m.Reduction).SequenceEqual(new double[]{50,100,100,30,30,1}),
                    "The running arm uses the installed six reduction ratios");
                var settingsDialog=new SmallProject.Dialogs.ConfigDialog();
                var stagedSettings=(SmallProject.Configs.JConfiguration)settingsDialog.DataContext;
                Check(!ReferenceEquals(stagedSettings,App.JConfig),"Settings edits are staged until Apply");
                double savedVelocity=App.JConfig.MotorMaxVelocity;
                stagedSettings.MotorMaxVelocity=1;
                Check(App.JConfig.MotorMaxVelocity==savedVelocity,"Editing the dialog cannot change live motion limits");
                settingsDialog.Close();
                var installed=new double[]{50,100,100,30,30,1};
                var fastTimes=new System.Collections.Generic.List<double>();var previousFast=zero;
                foreach(var target in recordedLoop) {
                    fastTimes.Add(MotionTiming.MinimumSeconds(previousFast,target,installed,new[]{0,1,2},App.JConfig));
                    previousFast=target;
                }
                var fastLoop=ContinuousPathPlanner.Build(testArm.dof6Solver.Model,zero,recordedLoop,fastTimes,
                    testArm.motorJ.Take(3).Select(m=>m.Direction*m.Reduction/360).ToArray(),true,
                    toleranceMm:3,jointIndices:new[]{0,1,2},maxVelocity:App.JConfig.MotorMaxVelocity,
                    maxAcceleration:App.JConfig.MotorMaxAcceleration,maxJerk:App.JConfig.MotorMaxJerk);
                foreach(var curve in fastLoop.ValidationPaths())
                    Check((await preview.ValidatePathAsync(curve)).ok,"The faster recorded loop passes actual collision validation");
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
                Check((await preview.ValidatePathAsync(new[]{zero,new double[]{0,40,-40,0,0,0},zero})).ok,
                    "Fixed test route follows the installed-axis model without collision");
                Check((await preview.ValidateMotionAsync(zero, new double[] {0,0,0,0,360,0})).ok, "J5 +360 path");
                Check((await preview.ValidateMotionAsync(zero, new double[] {0,0,0,0,-90,0})).ok, "J5 -90 path");
                Check((await preview.ValidateMotionAsync(zero, new double[] {0,0,0,-140,0,0})).ok, "J4 -140 path");
                Check((await preview.ValidateMotionAsync(zero, new double[] {0,0,0,50,0,0})).ok, "J4 +50 path");
                Check(!(await preview.ValidateMotionAsync(zero, new double[] {0,0,0,60,0,0})).ok, "J4 obstruction");
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
                foreach(double angle in new[]{-10.0,-150.0}) {
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
                var selections=new[]{window.cb_MoveJ1,window.cb_MoveJ2,window.cb_MoveJ3,window.cb_MoveJ4,window.cb_MoveJ5,window.cb_MoveJ6};
                var inputs=new[]{window.tb_Joint1,window.tb_Joint2,window.tb_Joint3,window.tb_Joint4,window.tb_Joint5,window.tb_Joint6};
                foreach(var selection in selections) selection.IsChecked=false;
                Check(!window.JykerKinematicContrl.TryReadMotionTargets(Array.Empty<int>(),out _),
                    "No selected axis cannot initiate movement");
                for(int mask=1;mask<64;mask++) {
                    foreach(var selection in selections) selection.IsChecked=false;
                    for(int axis=0;axis<6;axis++) inputs[axis].Text="invalid";
                    // Reset after deliberately putting invalid text in excluded fields.
                    foreach(var selection in selections) selection.IsChecked=false;
                    int[] axes=Enumerable.Range(0,6).Where(i=>(mask&(1<<i))!=0).ToArray();
                    foreach(int axis in axes) inputs[axis].Text=axis%2==0 ? "0" : "0.5";
                    Check(window.JykerKinematicContrl.SelectedMotionAxes.SequenceEqual(axes),
                        $"Editing targets automatically selects exactly subset {mask:X2}");
                    Check(window.JykerKinematicContrl.TryReadMotionTargets(axes,out var selectedTargets) &&
                        axes.All(i=>selectedTargets[i]==(i%2==0 ? 0 : .5)),
                        $"Subset {mask:X2} accepts zero targets and ignores excluded invalid inputs");
                    window.JykerKinematicContrl.ApplyModelJoints(zero);
                    Check(window.JykerKinematicContrl.SelectedMotionAxes.SequenceEqual(axes),
                        $"Refreshing displayed angles preserves subset {mask:X2}");
                }
                window.cb_MoveJ4.IsChecked=false;
                Check(!window.JykerKinematicContrl.SelectedMotionAxes.Contains(3),"Manual uncheck excludes the joint");
                foreach(var selection in selections) selection.IsChecked=false;
                var legacyRecord=Newtonsoft.Json.JsonConvert.DeserializeObject<SmallProject.JointMoveRecord.JointRecordModel>(
                    "{\"J1\":1,\"J2\":2,\"J3\":3,\"J4\":4,\"J5\":5,\"J6\":6}")!;
                Check(legacyRecord.AxisMask==63 && legacyRecord.ContainsAxes(Enumerable.Range(0,6).ToArray()),
                    "Legacy point records without a mask retain all six measured axes");
                for(int mask=1;mask<64;mask++) {
                    int[] axes=Enumerable.Range(0,6).Where(i=>(mask&(1<<i))!=0).ToArray();
                    var recordQueries=new System.Collections.Generic.List<int>();
                    var measured=await testArm.ReadPositionsAsync(node=>{
                        recordQueries.Add(node); testArm.RecievePos(node,0,true);
                    },()=>true,100,axes);
                    Check(measured!=null && recordQueries.SequenceEqual(axes.Select(i=>i+1)),
                        $"Point subset {mask:X2} records without querying missing boards");
                    var record=new SmallProject.JointMoveRecord.JointRecordModel {
                        AxisMask=mask,J1=0,J2=0,J3=0,J4=0,J5=0,J6=0 };
                    string json=Newtonsoft.Json.JsonConvert.SerializeObject(record);
                    var restored=Newtonsoft.Json.JsonConvert.DeserializeObject<SmallProject.JointMoveRecord.JointRecordModel>(json)!;
                    Check(restored.AxisMask==mask && restored.ContainsAxes(axes) && !json.Contains("RecordedJ1"),
                        $"Point subset {mask:X2} persists only its data and restores measured axes");
                    var displays=new[]{restored.RecordedJ1,restored.RecordedJ2,restored.RecordedJ3,
                        restored.RecordedJ4,restored.RecordedJ5,restored.RecordedJ6};
                    Check(Enumerable.Range(0,6).All(i=>axes.Contains(i) ? displays[i]==0 : displays[i]==null),
                        $"Point subset {mask:X2} distinguishes measured zero from unrecorded joints");
                    Check((restored.RecordedX.HasValue)==(mask==63),
                        $"Point subset {mask:X2} shows tool pose only with a full measured configuration");
                    var current=new double[]{.1,.2,.3,.4,.5,.6};
                    var target=restored.MotionTarget(current,axes);
                    Check(Enumerable.Range(0,6).All(i=>target[i]==(axes.Contains(i) ? 0 : current[i])),
                        $"Point subset {mask:X2} replays zero targets while holding excluded joints at the current position");
                    if(mask!=63) {
                        int missingAxis=Enumerable.Range(0,6).First(i=>!axes.Contains(i));
                        bool rejectedMissing=false;
                        try { restored.MotionTarget(current,new[]{missingAxis}); }
                        catch(ArgumentException) { rejectedMissing=true; }
                        Check(!restored.ContainsAxes(new[]{missingAxis}) && rejectedMissing,
                            $"Point subset {mask:X2} cannot replay an unmeasured cached axis");
                    }
                    var planFromRecord=ContinuousPathPlanner.Build(testArm.dof6Solver.Model,current,new[]{target},new[]{1.0},
                        axes.Select(i=>testArm.motorJ[i].Direction*testArm.motorJ[i].Reduction/360).ToArray(),false,jointIndices:axes);
                    var frames=MultiAxisProtocol.Encode(planFromRecord,axes,false);
                    Check(frames[0].Data.SequenceEqual(new byte[]{4,(byte)mask}) &&
                        frames.Where(c=>c.Code==0x51 || c.Code==0x53).All(c=>axes.Contains(c.Data[0]-1)),
                        $"Point subset {mask:X2} uploads only participating measured axes");
                }
                foreach(int invalidMask in new[]{0,64,-1})
                    Check(!new SmallProject.JointMoveRecord.JointRecordModel{AxisMask=invalidMask}.ContainsAxes(new[]{0}),
                        "Malformed point mask cannot authorize an axis");
                for(int count=2;count<=5;count++) {
                    int[] axes=Enumerable.Range(1,count).ToArray();
                    var subsetQueries=new System.Collections.Generic.List<int>();
                    Check(await testArm.ReadPositionsAsync(node=>{subsetQueries.Add(node);testArm.RecievePos((byte)node,0,true);},
                        ()=>true,100,axes)!=null && subsetQueries.SequenceEqual(axes.Select(i=>i+1)),
                        $"{count}-axis feedback waits only for participating nodes");
                    Check(await testArm.ReadPositionsAsync(node=>{if(node!=axes[^1]+1)testArm.RecievePos((byte)node,0,true);},
                        ()=>true,60,axes)==null,$"{count}-axis feedback rejects one missing participating node");
                }
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
