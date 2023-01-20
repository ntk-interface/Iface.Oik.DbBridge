using Iface.Oik.Tm.Helpers;
using Iface.Oik.Tm.Interfaces;
using Microsoft.Extensions.Hosting;

namespace OikTask
{
    public class ServerService : CommonServerService, IHostedService
    {
    }
    public class TmStartup : BackgroundService
    {
        private const string ApplicationName = "Iface.Oik.DB-Bridge";
        private const string TraceName = "OikDBBridge";
        private const string TraceComment = "<DB Bridge>";

        private static int _tmCid;
        private static TmUserInfo? _userInfo;
        private static TmServerFeatures _serverFeatures;
        private static IntPtr _stopEventHandle;

        private readonly IHostApplicationLifetime _applicationLifetime;
        private readonly ICommonInfrastructure _infr;

        public TmStartup(ICommonInfrastructure infr, IHostApplicationLifetime applicationLifetime)
        {
            _infr = infr;
            _applicationLifetime = applicationLifetime;
        }
        public static void Connect()
        {
            string tmpPipe = "TMS", tmpHost = ".", tmpUser = "", tmpPassword = "", remoteConfFile="";
            var commandLineArgs = Environment.GetCommandLineArgs();
            for(int i = 0; i< commandLineArgs.Length; i++)
            {
                var arg = commandLineArgs[i];
                if(arg.StartsWith("/f", StringComparison.OrdinalIgnoreCase))  // Файл конфигурации
                { 
                    remoteConfFile = arg.Substring(2);
                }
                else if (arg.StartsWith("/u", StringComparison.OrdinalIgnoreCase))  // Пользователь
                {
                    tmpUser = arg.Substring(2);
                }
                else if (arg.StartsWith("/s", StringComparison.OrdinalIgnoreCase))  // Пароль
                {
                    tmpPassword = arg.Substring(2);
                }
                else if (arg.StartsWith("/p", StringComparison.OrdinalIgnoreCase))  // Период
                {
                    string fullPeriod = arg.Substring(2);
                    var splittedPeriod = fullPeriod.Split('-');
                    if(splittedPeriod.Length > 1)
                    {
                        if (Int32.TryParse(splittedPeriod[0], out var period))
                            Worker.period = period;
                        if (Int32.TryParse(splittedPeriod[1], out var offset))
                            Worker.offset = offset;
                    }
                    else
                    {
                        if(Int32.TryParse(fullPeriod, out var period))
                            Worker.period = period;
                    }
                }
                else if (arg.StartsWith("/d", StringComparison.OrdinalIgnoreCase))  // Параметры соединения с БД
                {
                    Worker.ConnectionString = arg.Substring(2);
                }
                else
                {
                    if(i == 0) // имя программы
                    {
                        remoteConfFile = "_"+Path.GetFileNameWithoutExtension(arg)+".cfg";
                    }
                    else if(i == 1) // первый параметр - тм-сервер
                    {
                        tmpPipe = arg;
                    }
                    else if(i == 2) // второй параметр - компьютер
                    {
                        tmpHost = arg;
                    }    
                }
            }
            (_tmCid, _userInfo, _serverFeatures, _stopEventHandle) =
              Tms.InitializeAsTaskWithoutSql(new TmOikTaskOptions
                                                {
                                                    TraceName = TraceName,
                                                    TraceComment = TraceComment,
                                                },
                                             new TmInitializeOptions
                                                {
                                                    ApplicationName = ApplicationName,
                                                    TmServer = tmpPipe,
                                                    Host = tmpHost,
                                                    User = tmpUser,
                                                    Password = tmpPassword,
                                                });

            Tms.PrintMessage("Соединение с сервером установлено");
            //  прочитаем файл конфигурации
            var cfCid = Tms.Native.TmcGetCfsHandle(_tmCid);
            if (cfCid != IntPtr.Zero)
            {
                byte[] machine = new byte[1024], pipe = new byte[1024];

                Tms.Native.TmcGetCurrentServer(_tmCid, ref machine, (uint)machine.Length-1, ref pipe, (uint)pipe.Length-1);
                string s_pipe = System.Text.Encoding.Default.GetString(pipe);
                s_pipe = s_pipe.Remove(s_pipe.IndexOf('\0'));
                remoteConfFile = "TM_SERVER\\" + s_pipe + "\\" + remoteConfFile;

                var localTempConfFile = Path.GetTempFileName();

                const int errStringLength = 1000;
                var errString = new byte[errStringLength];
                uint errCode = 0;
                if (!Tms.Native.CfsFileGet(cfCid, remoteConfFile, localTempConfFile, 30000, IntPtr.Zero,
                                       out errCode, ref errString, errStringLength))
                {
                    Tms.PrintError("Ошибка при чтении файла с SQL запросом (" + remoteConfFile + ")");
                    Environment.Exit(-1);
                }
                else
                {
                    Worker.aSQL = File.ReadAllText(localTempConfFile);
                    File.Delete(localTempConfFile);
                }
            }
        }
        public override Task StartAsync(CancellationToken cancellationToken)
        {
            _infr.InitializeTmWithoutSql(_tmCid, _userInfo, _serverFeatures);
            return base.StartAsync(cancellationToken);
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (await Task.Run(() => Tms.StopEventSignalDuringWait(_stopEventHandle, 1000), stoppingToken))
                {
                    Tms.PrintMessage("Получено сообщение об остановке со стороны сервера");
                    _applicationLifetime.StopApplication();
                    break;
                }
            }
        }
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            Tms.TerminateWithoutSql(_tmCid);
            _infr.TerminateTm();

            Tms.PrintMessage("Задача будет закрыта");

            await base.StopAsync(cancellationToken);
        }
    }
}