using Iface.Oik.Tm.Helpers;
using Iface.Oik.Tm.Interfaces;
using Microsoft.Extensions.Hosting;
using MySql.Data;
using MySql.Data.MySqlClient;
using System.Reflection.PortableExecutable;
using static Iface.Oik.Tm.Native.Interfaces.TmNativeDefs;

namespace OikTask
{
    public class Worker : BackgroundService
    {
        public static string? aSQL;
        public static string? ConnectionString;
        public static int period=1;
        public static int offset=0;
        
        private const int WorkerDelay = 100;
        private long lasttime;
        private readonly ICommonInfrastructure _infr;
        private readonly IOikDataApi _api;


        public Worker(ICommonInfrastructure infr,
                      IOikDataApi api)
        {
            _infr = infr;
            _api = api;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            lasttime = GetSeconds() / period;
            while (!stoppingToken.IsCancellationRequested)
            {
                if (period >= 1)
                {
                    long curtime = (GetSeconds() + offset) / period;
                    if (curtime == lasttime)
                        continue;
                    lasttime = curtime;
                    if (((GetSeconds() + offset) % period) > 5)
                        continue;
                }
                await DoWork();
                await Task.Delay(WorkerDelay, stoppingToken);
            }
        }
        public async Task DoWork()
        {
            Tms.PrintMessage("Исполняем SQL: " + aSQL);
            try
            {

                var sb = new MySqlConnectionStringBuilder()
                {
                    Server = "localhost",
                    UserID = "root",
                    Password = "julia",
                    Database = "oik"
                };

                using (var conn = new MySqlConnection(sb.ConnectionString))
                {
                    var cmd = new MySqlCommand(aSQL, conn);
                    try
                    {
                        conn.Open();
                        MySqlDataReader dr = cmd.ExecuteReader();
                        while (dr.Read())
                        {
                            string line = "";
                            for (int i = 0; i < dr.FieldCount; i++)
                            {
                                line += dr[i].ToString()+'\t';
                            }
                            Console.WriteLine(line);
                        }
                    }
                    catch (Exception ex)
                    {
                        Tms.PrintError(ex.Message);
                    }
                }
            }
            catch(Exception ex)
            {
                Tms.PrintError(ex.Message);
            }
        }
        public static long GetSeconds()
        {
            TimeSpan timeSpan = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0);
            return (long)timeSpan.TotalSeconds;
        }
    }
}