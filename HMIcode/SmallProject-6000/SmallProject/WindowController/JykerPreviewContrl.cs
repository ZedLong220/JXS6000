using Microsoft.Web.WebView2.Core;
using SmallProject.Logger;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;

namespace SmallProject.WindowController
{
    internal sealed class JykerPreviewContrl
    {
        private const string Host = "jxs6000.local";
        private readonly MainWindow window;
        private bool ready;
        private readonly TaskCompletionSource<bool> readySignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private double[] desired = new double[6];
        private readonly Dictionary<string, TaskCompletionSource<(bool ok, string reason)>> pending = new();

        public JykerPreviewContrl(MainWindow window) => this.window = window;

        public async Task InitializeAsync()
        {
            try
            {
                string folder = Path.Combine(AppContext.BaseDirectory, "Viewer");
                if (!File.Exists(Path.Combine(folder, "index.html"))) throw new FileNotFoundException("缺少 Viewer 模型资源");
                string profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jyker", "JXS6000", "WebView2");
                var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profile);
                await window.RobotPreview.EnsureCoreWebView2Async(environment);
                var core = window.RobotPreview.CoreWebView2;
                core.SetVirtualHostNameToFolderMapping(Host, folder, CoreWebView2HostResourceAccessKind.DenyCors);
                core.NavigationStarting += (_, e) => {
                    if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != Host) e.Cancel = true;
                    else ready = false;
                };
                core.NewWindowRequested += (_, e) => e.Handled = true;
                core.WebMessageReceived += MessageReceived;
                core.ProcessFailed += (_, _) => { ready = false; FailPending(); window.PreviewStatus.Text = "三维预览进程停止，请重启上位机"; };
                core.Navigate($"https://{Host}/index.html");
            }
            catch (Exception ex)
            {
                window.PreviewStatus.Text = "三维预览加载失败：" + ex.Message;
                JLog.Info(window.PreviewStatus.Text);
                readySignal.TrySetException(ex);
            }
        }

        private void MessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (!Uri.TryCreate(e.Source, UriKind.Absolute, out var source) || source.Host != Host || source.Scheme != "https") return;
            try
            {
                using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                var root = doc.RootElement;
                switch (root.GetProperty("type").GetString())
                {
                    case "ready":
                        ready = true;
                        window.PreviewStatus.Text = "JXS6000 · 预览操作不发送电机指令 · 右侧关节为 URDF 角度";
                        SetJoints(desired);
                        readySignal.TrySetResult(true);
                        break;
                    case "previewChanged":
                        var values = JsonSerializer.Deserialize<double[]>(root.GetProperty("angles").GetRawText());
                        if (values != null) window.JykerKinematicContrl.ApplyModelJoints(values, false);
                        break;
                    case "validationResult":
                        string id = root.GetProperty("requestId").GetString() ?? "";
                        if (pending.Remove(id, out var completion))
                        {
                            bool ok = root.GetProperty("ok").GetBoolean();
                            completion.TrySetResult((ok, ok ? "" : root.GetProperty("reason").GetString() ?? "碰撞检查失败"));
                        }
                        break;
                }
            }
            catch (Exception ex) { JLog.Info("预览消息无效：" + ex.Message); }
        }

        public void SetJoints(double[] angles)
        {
            desired = (double[])angles.Clone();
            if (ready) window.RobotPreview.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "setJoints", angles }));
        }

        internal Task WaitUntilReadyAsync() => readySignal.Task.WaitAsync(TimeSpan.FromSeconds(25));
        internal Task<string> ReadDiagnosticStateAsync() => window.RobotPreview.CoreWebView2.ExecuteScriptAsync(@"
            (() => {
                const r = window.robot;
                r.updateMatrixWorld(true);
                return {
                    joints: [1,2,3,4,5,6].map(i => r.joints['joint_'+i].angle * 180 / Math.PI),
                    colliders: Object.keys(r.colliders).length,
                    hidden: Object.values(r.colliders).every(c => !c.visible),
                    tool: r.links.base_link.matrixWorld.clone().invert().multiply(r.links.tool0.matrixWorld).elements,
                    width: document.querySelector('canvas').width,
                    height: document.querySelector('canvas').height
                };
            })()");

        public async Task<(bool ok, string reason)> ValidateMotionAsync(double[] from, double[] to)
        {
            if (!ready) return (false, "三维模型尚未就绪，无法检查运动路径");
            string id = Guid.NewGuid().ToString("N");
            var completion = new TaskCompletionSource<(bool, string)>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending.Add(id, completion);
            try
            {
                window.RobotPreview.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "validateMotion", requestId = id, from, to }));
                return await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (Exception ex) { return (false, "运动路径检查失败：" + ex.Message); }
            finally { pending.Remove(id); }
        }

        public async Task<(bool ok, string reason)> ValidatePathAsync(IReadOnlyList<double[]> points, CancellationToken cancellationToken = default)
        {
            if (!ready) return (false, "三维模型尚未就绪");
            // Bound each browser operation; this also keeps Stop responsive
            // between chunks on long densely sampled paths.
            const int chunkSize=160;
            for (int offset=0; offset<points.Count; offset+=chunkSize-1)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunk=new List<double[]>();
                for (int i=offset;i<Math.Min(points.Count,offset+chunkSize);++i) chunk.Add(points[i]);
                string id=Guid.NewGuid().ToString("N");
                var completion=new TaskCompletionSource<(bool,string)>(TaskCreationOptions.RunContinuationsAsynchronously);
                pending.Add(id,completion);
                try
                {
                    window.RobotPreview.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new {type="validatePath",requestId=id,points=chunk}));
                    var result=await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
                    if (!result.Item1) return result;
                }
                catch (Exception ex) { return (false,"连续路径检查失败："+ex.Message); }
                finally { pending.Remove(id); }
                if (offset+chunkSize>=points.Count) break;
            }
            return (true,"");
        }

        private void FailPending()
        {
            foreach (var completion in pending.Values) completion.TrySetResult((false, "预览进程已停止"));
            pending.Clear();
        }
    }
}
