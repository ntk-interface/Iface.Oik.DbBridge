using Iface.Oik.Tm.Helpers;
using Iface.Oik.Tm.Interfaces;
using Iface.Oik.Tm.Utils;
using Microsoft.Extensions.Hosting;
using MySql.Data.MySqlClient;
using Npgsql;
using System.Data.SqlClient;
using System.Data;
using System.Data.Common;
using System.Globalization;
using Org.BouncyCastle.Asn1;

namespace OikTask
{
    public class Worker : BackgroundService
    {
        private static string? aSQL;
        private static string? connectionString;
        private static int period=10;
        private static int offset=0;
        
        private const int WorkerDelay = 100;
        private long lasttime;

        private DbConnection? dbConnection;
        private DbCommand? dbCommand;

        private readonly ICommonInfrastructure _infr;
        private readonly IOikDataApi _api;

        public Worker(ICommonInfrastructure infr,
                      IOikDataApi api)
        {
            _infr = infr;
            _api = api;
        }
        public static void Initialize(string _connectionString, string _aSQL, int _period, int _offset)
        {
            connectionString = _connectionString;
            aSQL = _aSQL;
            period = _period;
            offset = _offset;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if(connectionString == null)
            {
                Tms.PrintError("Не заданы параметры соединения (/dтип,сервер,бд,пользователь,пароль)");
                return;
            }
            var connection_params = connectionString.Split(',');
            string dbType     = connection_params.ElementAtOrDefault(0) ?? "";
            string dbServer   = connection_params.ElementAtOrDefault(1) ?? "";
            string dbDatabase = connection_params.ElementAtOrDefault(2) ?? "";
            string dbUserID   = connection_params.ElementAtOrDefault(3) ?? "";
            string dbPassword = connection_params.ElementAtOrDefault(4) ?? "";

            // Создание специфических объектов БД в зависимости от типа
            switch (dbType.ToUpper())
            {
                case "MS":
                    dbConnection = new SqlConnection(new SqlConnectionStringBuilder
                    {
                        DataSource = dbServer,
                        InitialCatalog = dbDatabase,
                        UserID = dbUserID,
                        Password = dbPassword,
                        TrustServerCertificate = true
                    }.ConnectionString);
                    dbCommand = (dbConnection as SqlConnection)!.CreateCommand();
                    break;
                case "MY":
                    dbConnection = new MySqlConnection(new MySqlConnectionStringBuilder
                        {
                            Server   = dbServer,
                            Database = dbDatabase,
                            UserID   = dbUserID,
                            Password = dbPassword
                        }.ConnectionString);
                    dbCommand = (dbConnection as MySqlConnection)!.CreateCommand();
                    break;
                case "PG":
                    dbConnection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder
                        {
                            Host     = dbServer,
                            Database = dbDatabase,
                            Username = dbUserID,
                            Password = dbPassword
                        }.ConnectionString);
                    dbCommand = (dbConnection as NpgsqlConnection)!.CreateCommand();
                    break;
                default:
                    Tms.PrintError("Неподдерживаемый тип базы данных ("+dbType+")");
                    return;
            }
            // Запуск исполнения по границе периода
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
                await DoWork().ConfigureAwait(false);
                await Task.Delay(WorkerDelay, stoppingToken).ConfigureAwait(false);
            }
            if(dbCommand != null) dbCommand.Dispose();
            if(dbConnection != null) dbConnection.Dispose();
        }
        async Task DoWork()
        {
            if ((dbConnection == null) || (dbCommand == null) || (aSQL == null))
                return;
            try
            {
                var statements = aSQL.Split(';');
                foreach ( var statement in statements )
                {
                    if(statement.Trim().IsNullOrEmpty())
                    { continue; }
                    // Части выражения, выделенные %..%, обрабатываются на сервере ТМ
                    string parsed_statement = "";
                    var tokens = statement.Split("%");
                    for (int i = 0; i < tokens.Length; i++)
                    {
                        if ((i % 2) == 0)
                        {
                            parsed_statement += tokens[i];
                        }
                        else
                        { 
                            string res = await _api.GetExpressionResult(tokens[i]).ConfigureAwait(false);
                            if(float.TryParse(res, NumberStyles.Any, CultureInfo.InvariantCulture, out var f_res))
                                parsed_statement += res;
                            else
                                parsed_statement += "ERR";
                            Tms.PrintDebug(tokens[i]+"="+res);
                        }
                    }
                    Tms.PrintDebug("Исполняем SQL: " + parsed_statement);
                    dbCommand.CommandText = parsed_statement;
                    await dbConnection.OpenAsync().ConfigureAwait(false);
                    if (parsed_statement.Trim().StartsWith("select", StringComparison.OrdinalIgnoreCase))
                    {
                        DbDataReader dr = await dbCommand.ExecuteReaderAsync().ConfigureAwait(false);
                        while (dr.Read())
                        {
                            string line = "";
                            for (int i = 0; i < dr.FieldCount; i++)
                            {
                                line += dr[i].ToString() + '\t';
                            }
                            Tms.PrintDebug(line);
                            if(dr.FieldCount == 5)
                            {
                                string c_type = dr[0].ToString() ?? "";
                                string ch     = dr[1].ToString() ?? ""; short.TryParse(ch,    out var i_ch);
                                string rtu    = dr[2].ToString() ?? ""; short.TryParse(rtu,   out var i_rtu);
                                string point  = dr[3].ToString() ?? ""; short.TryParse(point, out var i_point);
                                string value  = dr[4].ToString() ?? "";
                                switch(c_type.ToUpper())
                                {
                                    case "#TT":
                                        if(float.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var f_value))
                                        {
                                            await _api.SetAnalog(i_ch, i_rtu, i_point, f_value).ConfigureAwait(false);
                                        }
                                        else
                                        {
                                            Tms.Native.TmcSetAnalogFlags(_infr.TmCid, i_ch, i_rtu, i_point, (short)TmFlags.Unreliable);
                                        }
                                        break;
                                    case "#TC":
                                        if(short.TryParse(value, out var i_value))
                                        {
                                            await _api.SetStatus(i_ch, i_rtu, i_point, i_value).ConfigureAwait(false);
                                        }
                                        else
                                        {
                                            Tms.Native.TmcSetStatusFlags(_infr.TmCid, i_ch, i_rtu, i_point,(short)TmFlags.Unreliable);
                                        }
                                        break;        
                                }
                            }
                        }
                    }
                    else
                    {
                        int number = await dbCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
                        Tms.PrintDebug("Изменено объектов: "+number.ToString());
                    }
                    dbConnection.Close();
                }
            }
            catch (Exception ex)
            {
                Tms.PrintError(ex.Message);
                dbConnection.Close();
            }
        }       
        public static long GetSeconds()
        {
            TimeSpan timeSpan = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0);
            return (long)timeSpan.TotalSeconds;
        }
    }
}