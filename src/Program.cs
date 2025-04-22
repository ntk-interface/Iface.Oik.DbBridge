using System;
using System.Text;
using Iface.Oik.Tm.Api;
using Iface.Oik.Tm.Helpers;
using Iface.Oik.Tm.Interfaces;
using Iface.Oik.Tm.Native.Api;
using Iface.Oik.Tm.Native.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Iface.Oik.DbBridge;

public class Program
{
  public static void Main(string[] args)
  {
    // требуется для работы с кодировкой Win-1251
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

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
        .ConfigureServices((_, services) =>
         {
           // регистрация сервисов ОИК
           services.AddSingleton<ITmNative, TmNative>();
           services.AddSingleton<ITmsApi, TmsApi>();
           services.AddSingleton<IOikSqlApi, OikSqlApi>();
           services.AddSingleton<IOikDataApi, OikDataApi>();
           services.AddSingleton<ICommonInfrastructure, CommonInfrastructure>();
           services.AddSingleton<ServerService>();
           services.AddSingleton<ICommonServerService>(provider => provider.GetRequiredService<ServerService>());

           // регистрация фоновых служб
           services.AddHostedService<TmStartup>();
           services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<ServerService>());
           services.AddHostedService<Worker>();
         });
}