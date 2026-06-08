using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Iface.Oik.Tm.Helpers;
using Iface.Oik.Tm.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using MySqlConnector;
using Npgsql;

namespace Iface.Oik.DbBridge;

public class Worker : BackgroundService
{
  private Config _config = default!;

  private          string       _connectionString = string.Empty;
  private readonly List<string> _commandTexts     = new();


  private readonly IOikDataApi              _api;
  private readonly IHostApplicationLifetime _applicationLifetime;


  public Worker(IOikDataApi              api,
                IHostApplicationLifetime applicationLifetime)
  {
    _api                 = api;
    _applicationLifetime = applicationLifetime;
  }


  public override async Task StartAsync(CancellationToken cancellationToken)
  {
    try
    {
      _config = ConfigLoader.Load();

      if (string.IsNullOrEmpty(_config.SqlText))
      {
        throw new Exception("Не задан текст SQL запроса");
      }

      _commandTexts.AddRange(_config.SqlText.Split(';', StringSplitOptions.TrimEntries |
                                                        StringSplitOptions.RemoveEmptyEntries));

      Tms.PrintMessage("Загружена конфигурация");

      await ValidateDbConnectionAndThrow(cancellationToken);

      Tms.PrintMessage("Соединение с базой данных проверено, начинаю работу");

      await base.StartAsync(cancellationToken);
    }
    catch (Exception ex)
    {
      Tms.PrintError($"Ошибка: {ex.Message}");
      _applicationLifetime.StopApplication();
    }
  }


  private async Task ValidateDbConnectionAndThrow(CancellationToken cancellationToken)
  {
    _connectionString = PrepareConnectionString();

    await using var db = GetDbConnection();
    await db.OpenAsync(cancellationToken);
  }


  private string PrepareConnectionString()
  {
    return _config.DbType.ToUpper() switch
           {
             "MSSQL" => new SqlConnectionStringBuilder
             {
               DataSource             = $"{_config.DbHost},{_config.DbPort}",
               InitialCatalog         = _config.DbDatabase,
               UserID                 = _config.DbUser,
               Password               = _config.DbPassword,
               TrustServerCertificate = false,
               Encrypt                = false,
             }.ConnectionString,

             "MYSQL" => new MySqlConnectionStringBuilder
             {
               Server   = _config.DbHost,
               Port     = (uint)_config.DbPort,
               Database = _config.DbDatabase,
               UserID   = _config.DbUser,
               Password = _config.DbPassword,
               SslMode  = MySqlSslMode.Disabled,
             }.ConnectionString,

             "PGSQL" => new NpgsqlConnectionStringBuilder
             {
               Host              = _config.DbHost,
               Port              = _config.DbPort,
               Database          = _config.DbDatabase,
               Username          = _config.DbUser,
               Password          = _config.DbPassword,
               SslMode           = SslMode.Disable,
               GssEncryptionMode = GssEncryptionMode.Disable,
             }.ConnectionString,

             _ => throw new Exception($"Неизвестный тип базы данных {_config.DbType}"),
           };
  }


  private DbConnection GetDbConnection()
  {
    return _config.DbType.ToUpper() switch
           {
             "MSSQL" => new SqlConnection(_connectionString),
             "MYSQL" => new MySqlConnection(_connectionString),
             "PGSQL" => new NpgsqlConnection(_connectionString),
             _       => throw new Exception($"Неизвестный тип базы данных {_config.DbType}"),
           };
  }


  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        int delayMs = CalculateDelay();
        await Task.Delay(delayMs, stoppingToken);
        if (stoppingToken.IsCancellationRequested)
          break;


        await DoWork(stoppingToken);
      }
      catch (Exception ex)
      {
        Tms.PrintDebug($"Ошибка: {ex.Message}");
      }
    }
  }


  private int CalculateDelay()
  {
    DateTime now = DateTime.Now;

    TimeSpan period = TimeSpan.FromSeconds(_config.WorkPeriod);
    TimeSpan offset = TimeSpan.FromSeconds(_config.WorkOffset);

    // Вычисляем ближайшую границу
    long     ticksSinceEpoch = now.Ticks;
    long     periodTicks     = period.Ticks;
    long     periods         = ticksSinceEpoch / periodTicks;
    DateTime nextPeriodStart = new DateTime((periods + 1) * periodTicks, DateTimeKind.Local);

    DateTime nextRun = nextPeriodStart - offset;

    Tms.PrintDebug("Следующий запуск в " + nextRun);

    // Проверка на прошлое
    if (nextRun <= now)
    {
      nextPeriodStart += period;
      nextRun         =  nextPeriodStart - offset;
    }

    TimeSpan delay   = nextRun - now;
    int      delayMs = (int)delay.TotalMilliseconds;


    return Math.Max(100, delayMs); //минимум 100мс
  }


  private async Task DoWork(CancellationToken stoppingToken)
  {
    await using var db = GetDbConnection();

    try
    {
      await db.OpenAsync(stoppingToken);

      foreach (var rawCommandText in _commandTexts)
      {
        // подменяем выражения внутри процентов на результат выражения, например %TT20:1:1% -> 30.156
        var commandText = Regex.Replace(rawCommandText, @"%([^%]+)%", m =>
        {
          var expression       = m.Groups[1].Value;
          var expressionResult = _api.GetExpressionResultSync(expression);
          Tms.PrintDebug($"{expression} -> {expressionResult}");
          return expressionResult;
        });

        Tms.PrintDebug("Исполняется SQL: " + commandText);

        if (commandText.Trim().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
        {
          var rows = await db.QueryAsync<(string Type, int Ch, int Rtu, int Point, float Value)>(
                       new CommandDefinition(commandText, cancellationToken: stoppingToken));

          foreach (var r in rows)
          {
            switch (r.Type)
            {
              case "#TC":
                await _api.SetStatus(r.Ch, r.Rtu, r.Point, (int)r.Value);
                Tms.PrintDebug($"#TC{r.Ch}:{r.Rtu}:{r.Point} <- {(int)r.Value}");
                break;

              case "#TT":
                await _api.SetAnalog(r.Ch, r.Rtu, r.Point, r.Value);
                Tms.PrintDebug($"#TT{r.Ch}:{r.Rtu}:{r.Point} <- {r.Value}");
                break;

              default:
                throw new Exception($"Неизвестный тип данных в колонке: {r.Type}");
            }
          }
        }
        else
        {
          var rowsCount = await db.ExecuteAsync(new CommandDefinition(commandText, cancellationToken: stoppingToken));
          Tms.PrintDebug($"Обработано строк: {rowsCount}");
        }
      }
    }
    catch (Exception ex)
    {
      Tms.PrintError(ex.Message);
    }
  }
}