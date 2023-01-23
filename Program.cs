using System;
using System.Text;
using Iface.Oik.Tm.Api;
using Iface.Oik.Tm.Helpers;
using Iface.Oik.Tm.Interfaces;
using Iface.Oik.Tm.Native.Api;
using Iface.Oik.Tm.Native.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace OikTask
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // требуется для работы с кодировкой Win-1251
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            if( Environment.GetCommandLineArgs().Length < 2 )
            {
                Console.WriteLine("\nПрограмма обмена данными между \"ОИК Диспетчер\" и сторонними СУБД\n\nИспользование:");
                Console.WriteLine("{0} сервер_тм компьютер_оик /pпериод_запуска /dтип,сервер,база,пользователь,пароль\n",
                    Path.GetFileNameWithoutExtension(Environment.GetCommandLineArgs()[0]));
                Console.WriteLine(
@"период_запуска - периодичность исполнения в секундах, можно указать исполнение чуть раньше.
Например /p60-1 запуск каждую минуту за секунду до наступления времени.
умолчание 10 секунд

тип - MS, MY, PG. Microsoft SQL, mySQL, PostgreSQL соответственно

Дополнительные параметры
/fфайл_с_запросом_на_сервере_ОИК
/uпользователь_ОИК
/sпароль_ОИК");                   
                    
                Environment.Exit(-1);
            }
            string _Pipe = "TMS", _Host = ".", _User = "", _Password = "", remoteConfFile = "";
            string _connectionString="";
            string _aSQL="";
            int _period = 10;
            int _offset = 0;

            var commandLineArgs = Environment.GetCommandLineArgs();
            for (int i = 0; i < commandLineArgs.Length; i++)
            {
                var arg = commandLineArgs[i];
                if (arg.StartsWith("/f", StringComparison.OrdinalIgnoreCase))  // Файл конфигурации
                {
                    remoteConfFile = arg.Substring(2);
                }
                else if (arg.StartsWith("/u", StringComparison.OrdinalIgnoreCase))  // Пользователь
                {
                    _User = arg.Substring(2);
                }
                else if (arg.StartsWith("/s", StringComparison.OrdinalIgnoreCase))  // Пароль
                {
                    _Password = arg.Substring(2);
                }
                else if (arg.StartsWith("/p", StringComparison.OrdinalIgnoreCase))  // Период
                {
                    string fullPeriod = arg.Substring(2);
                    var splittedPeriod = fullPeriod.Split('-');
                    if (splittedPeriod.Length > 1)
                    {
                        if (Int32.TryParse(splittedPeriod[0], out var period))
                            _period = period;
                        if (Int32.TryParse(splittedPeriod[1], out var offset))
                            _offset = offset;
                    }
                    else
                    {
                        if (Int32.TryParse(fullPeriod, out var period))
                            _period = period;
                    }
                }
                else if (arg.StartsWith("/d", StringComparison.OrdinalIgnoreCase))  // Параметры соединения с БД
                {
                    _connectionString = arg.Substring(2);
                }
                else
                {
                    if (i == 0) // имя программы
                    {
                        remoteConfFile = "_" + Path.GetFileNameWithoutExtension(arg) + ".cfg";
                    }
                    else if (i == 1) // первый параметр - тм-сервер
                    {
                        _Pipe = arg;
                    }
                    else if (i == 2) // второй параметр - компьютер
                    {
                        _Host = arg;
                    }
                }
            }
            // устанавливаем соединение с сервером ОИК
            int _tmCid=0;
            try
            {
                _tmCid = TmStartup.Connect(_Pipe, _Host, _User, _Password);
            }
            catch (Exception ex)
            {
                Tms.PrintError(ex.Message);
                Environment.Exit(-1);
            }
            //  прочитаем файл конфигурации
            var cfCid = Tms.Native.TmcGetCfsHandle(_tmCid);
            if (cfCid != IntPtr.Zero)
            {
                byte[] machine = new byte[1024], pipe = new byte[1024];

                Tms.Native.TmcGetCurrentServer(_tmCid, ref machine, (uint)machine.Length - 1, ref pipe, (uint)pipe.Length - 1);
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
                    _aSQL = File.ReadAllText(localTempConfFile);
                    File.Delete(localTempConfFile);
                }
            }
            else
            {
                Tms.PrintError("Не удалось получить доступ к серверу конфигурации");
                Environment.Exit(-1);
            }
            Worker.Initialize(_connectionString, _aSQL, _period, _offset);
            // .NET Generic Host
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
          Host.CreateDefaultBuilder(args)
              .ConfigureServices((hostContext, services) =>
              {
                  // регистрация сервисов ОИК
                  services.AddSingleton<ITmNative, TmNative>();
                  services.AddSingleton<ITmsApi, TmsApi>();
                  services.AddSingleton<IOikSqlApi, OikSqlApi>();
                  services.AddSingleton<IOikDataApi, OikDataApi>();
                  services.AddSingleton<ICommonInfrastructure, CommonInfrastructure>();
                  services.AddSingleton<ServerService>();
                  services.AddSingleton<ICommonServerService>(provider => provider.GetService<ServerService>());

                  // регистрация фоновых служб
                  services.AddHostedService<TmStartup>();
                  services.AddSingleton<IHostedService>(provider => provider.GetService<ServerService>());
                  services.AddHostedService<Worker>();
              });
    }
}