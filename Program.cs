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
            // устанавливаем соединение с сервером ОИК
            try
            {
                TmStartup.Connect();
            }
            catch (Exception ex)
            {
                Tms.PrintError(ex.Message);
                Environment.Exit(-1);
            }
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