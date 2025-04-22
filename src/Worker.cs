using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.SqlClient;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Iface.Oik.Tm.Helpers;
using Iface.Oik.Tm.Interfaces;
using Microsoft.Extensions.Hosting;
using MySqlConnector;
using Npgsql;

namespace Iface.Oik.DbBridge;

public class Worker : BackgroundService
{
  private Config _config = default!;

  private          string       _connectionString = string.Empty;
  private readonly List<string> _commandTexts     = new();

  private long _lastRunTime;


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

      await ValidateDbConnectionAndThrow();

      if (string.IsNullOrEmpty(_config.SqlText))
      {
        throw new Exception("Не задан текст SQL запроса");
      }

      _commandTexts.AddRange(_config.SqlText.Split(';', StringSplitOptions.TrimEntries |
                                                        StringSplitOptions.RemoveEmptyEntries));

      Tms.PrintDebug("Конфигурация загружена");

      await base.StartAsync(cancellationToken);
    }
    catch (Exception ex)
    {
      Tms.PrintError($"Ошибка: {ex.Message}");
      _applicationLifetime.StopApplication();
    }
  }


  private async Task ValidateDbConnectionAndThrow()
  {
    _connectionString = PrepareConnectionString();

    await using var db = GetDbConnection();
    await db.OpenAsync();
  }


  private string PrepareConnectionString()
  {
    return _config.DbType.ToUpper() switch
           {
             "MSSQL" => new SqlConnectionStringBuilder
             {
               DataSource             = _config.DbHost,
               InitialCatalog         = _config.DbDatabase,
               UserID                 = _config.DbUser,
               Password               = _config.DbPassword,
               TrustServerCertificate = true
             }.ConnectionString,

             "MYSQL" => new MySqlConnectionStringBuilder
             {
               Server   = _config.DbHost,
               Database = _config.DbDatabase,
               UserID   = _config.DbUser,
               Password = _config.DbPassword,
               SslMode  = MySqlSslMode.Disabled,
             }.ConnectionString,

             "PGSQL" => new NpgsqlConnectionStringBuilder
             {
               Host     = _config.DbHost,
               Database = _config.DbDatabase,
               Username = _config.DbUser,
               Password = _config.DbPassword,
               SslMode  = SslMode.Disable,
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
    // TODO переделать на более понятный вариант
    _lastRunTime = GetSeconds() / _config.WorkPeriod;

    while (!stoppingToken.IsCancellationRequested)
    {
      if (_config.WorkPeriod >= 1)
      {
        var currentTime = (GetSeconds() + _config.WorkOffset) / _config.WorkPeriod;
        if (currentTime == _lastRunTime)
        {
          continue;
        }

        _lastRunTime = currentTime;
        if (((GetSeconds() + _config.WorkOffset) % _config.WorkPeriod) > 5)
        {
          continue;
        }
      }

      await DoWork();
      await Task.Delay(100, stoppingToken);
    }

    long GetSeconds() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
  }


  private async Task DoWork()
  {
    await using var db = GetDbConnection();

    try
    {
      await db.OpenAsync();

      foreach (var rawCommandText in _commandTexts)
      {
        // TODO обрабатывать по-другому это, может через регулярные выражения
        var commandText = string.Empty;
        var tokens      = rawCommandText.Split("%");
        for (var i = 0; i < tokens.Length; i++)
        {
          if ((i % 2) == 0)
          {
            commandText += tokens[i];
          }
          else
          {
            var result = await _api.GetExpressionResult(tokens[i]);
            commandText += float.TryParse(result, NumberStyles.Any, CultureInfo.InvariantCulture, out _)
                             ? result
                             : "ERR";
            Tms.PrintDebug($"{tokens[i]} -> {result}");
          }
        }

        Tms.PrintDebug("Исполняется SQL: " + commandText);

        if (commandText.Trim().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
        {
          var rows = await db.QueryAsync<(string Type, int Ch, int Rtu, int Point, float Value)>(commandText);

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
          var rowsCount = await db.ExecuteAsync(commandText);
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