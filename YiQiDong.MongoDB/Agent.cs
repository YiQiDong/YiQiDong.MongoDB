using System.Diagnostics;
using YiQiDong.Core;
using YiQiDong.Core.Utils;
using YiQiDong.Agent;
using YiQiDong.MongoDB.Functions;
using YiQiDong.MongoDB.Utils;
using Quick.Utils;

namespace YiQiDong.MongoDB
{
    public class Agent : AbstractAgent
    {
        private string[][] serveiceStartLogKeys =
        [
            [
                "mongod startup complete"
            ]
        ];

        public static Agent Instance { get; private set; }
        /// <summary>
        /// 数据库服务是否已启动
        /// </summary>
        public bool DbServiceStarted { get; private set; } = false;
        private string imageFolder;

        public Process Process { get; set; }

        public Agent()
        {
            Instance = this;
        }

        public override void Init()
        {
            //注册编码提供程序(支持GB2312等编码)
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            base.Init();

            if (AgentContext.IsContainerRuning)
            {
                imageFolder = AgentContext.Container.ImageFolder;
                AddFunction(new Config());
            }
            else
            {
                imageFolder = AppContext.BaseDirectory;
                var dataFolder = imageFolder;
                new Config(imageFolder, dataFolder);
            }
        }

        protected override void Start()
        {
            var dataFolder = Config.Instance.GetDataFolder();
            Config.Instance.RefreshProperties(dataFolder);

            DbServiceStarted = false;
            //检查复制配置文件
            FileSystemUtils.CopyFile(Path.Combine(imageFolder, "mongod.conf"), dataFolder);

            //检查数据库目录是否初始化，如果不存在，则初始化
            if (!Directory.Exists(Path.Combine(dataFolder, "data")))
            {
                AgentContext.LogInfo("正在初始化数据库...");
                Directory.CreateDirectory(Path.Combine(dataFolder, "data"));
                AgentContext.LogInfo("初始化数据库时成功。");
            }
            Process = Process.Start(DbUtils.GetPsi(imageFolder, dataFolder));
            AgentContext.LogInfo("数据库监听端口：" + Config.Instance.GetConnectPort());
            AgentContext.LogInfo($"数据库服务进程[Id:{Process.Id},Name:{Process.ProcessName}]已经启动。");
            Process.EnableRaisingEvents = true;
            Process.OutputDataReceived += Process_OutputDataReceived;
            Process.ErrorDataReceived += Process_ErrorDataReceived;
            Process.BeginOutputReadLine();
            Process.BeginErrorReadLine();
            Process.Exited += Process_Exited;

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            //等待服务启动完成
            while (!Process.HasExited && !DbServiceStarted)
            {
                Thread.Sleep(1000);
                var totalMinutes = stopwatch.Elapsed.TotalMinutes;
                if (totalMinutes > 1)
                {
                    AgentContext.LogWarn("数据库服务经过了1分钟仍未启动完成...");
                    stopwatch.Restart();
                }
            }
            stopwatch.Stop();
            if (Process.HasExited)
                return;
            AgentContext.LogInfo("[数据库服务启动完成]");
        }

        private void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null)
                return;
            var line = e.Data;
            AgentContext.LogInfo(e.Data);
            //{"t":{"$date":"2026-11-26T10:23:33.007+08:00"},"s":"I",  "c":"CONTROL",  "id":8423403, "ctx":"initandlisten","msg":"mongod startup complete","attr":{"Summary of time elapsed":{"Startup from clean shutdown?":true,"Statistics":{"createLockFileMillis":0,"setUpPeriodicRunnerMillis":0,"setUpOCSPMillis":0,"setUpTransportLayerMillis":0,"initSyncCrashRecoveryMillis":0,"getStorageEngineMetadataMillis":0,"validateMetadataMillis":0,"createStorageEngineMillis":194,"initializeFCVForIndexMillis":0,"dropAbandonedIdentsMillis":0,"standaloneClusterParamsMillis":0,"userAndRolesGraphMillis":0,"waitForMajorityServiceMillis":0,"startUpReplCoordMillis":1,"recoverChangeStreamMillis":0,"logStartupOptionsMillis":0,"startUpTransportLayerMillis":0,"initAndListenTotalMillis":295}}}}
            //判断数据库服务启动完成
            if (!DbServiceStarted)
                DbServiceStarted = serveiceStartLogKeys.Any(t => t.All(u => line.Contains(u)));
        }

        private void Process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null)
                return;
            var line = e.Data;
            AgentContext.LogInfo(line);
            //{"t":{"$date":"2026-11-26T10:23:33.007+08:00"},"s":"I",  "c":"CONTROL",  "id":8423403, "ctx":"initandlisten","msg":"mongod startup complete","attr":{"Summary of time elapsed":{"Startup from clean shutdown?":true,"Statistics":{"createLockFileMillis":0,"setUpPeriodicRunnerMillis":0,"setUpOCSPMillis":0,"setUpTransportLayerMillis":0,"initSyncCrashRecoveryMillis":0,"getStorageEngineMetadataMillis":0,"validateMetadataMillis":0,"createStorageEngineMillis":194,"initializeFCVForIndexMillis":0,"dropAbandonedIdentsMillis":0,"standaloneClusterParamsMillis":0,"userAndRolesGraphMillis":0,"waitForMajorityServiceMillis":0,"startUpReplCoordMillis":1,"recoverChangeStreamMillis":0,"logStartupOptionsMillis":0,"startUpTransportLayerMillis":0,"initAndListenTotalMillis":295}}}}
            //判断数据库服务启动完成
            if (!DbServiceStarted)
                DbServiceStarted = serveiceStartLogKeys.Any(t => t.All(u => line.Contains(u)));
        }

        private void DetectProcessExitCode(int exitCode)
        {
            if (OperatingSystem.IsWindows())
            {
                switch (exitCode)
                {
                    //0xC0000135
                    case -1073741515:
                        AgentContext.LogInfo($"DLL文件加载失败，请确保已安装正确的Visual C++运行库。下载地址：https://learn.microsoft.com/zh-cn/cpp/windows/latest-supported-vc-redist");
                        break;
                }
            }
        }

        private void Process_Exited(object sender, EventArgs e)
        {
            AgentContext.LogInfo($"进程[Id:{Process.Id},Name:{Process.ProcessName}]已经退出，退出码：{Process.ExitCode}。");
            DetectProcessExitCode(Process.ExitCode);
            if (!AgentContext.IsContainerRuning)
                return;
            if (!AgentContext.Container.AutoStart)
                return;
            Task.Delay(5000, AgentStartCancellationToken).ContinueWith(t =>
            {
                if (t.IsCanceled)
                    return;
                Start();
            });
        }

        protected override void Stop()
        {
            var process = Process;
            if (process == null || process.HasExited)
                return;
            //发送关闭命令，最多等待30秒
            try
            {
                DbUtils.ShutdownDb(Config.Instance.GetConnectHost(), Config.Instance.GetConnectPort())
                    .Wait(30 * 1000);
            }
            catch (Exception ex)
            {
                AgentContext.LogError(ExceptionUtils.GetExceptionMessage(ex));
            }
            process = Process;
            if (process == null
                || process.HasExited)
                return;
            process.Kill(true);
        }
    }
}
