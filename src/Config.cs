namespace Iface.Oik.DbBridge;

public class Config
{
  public int WorkPeriod { get; set; } = 10;
  public int WorkOffset { get; set; } = 0;

  public string DbType     { get; set; } = string.Empty;
  public string DbHost     { get; set; } = string.Empty;
  public int    DbPort     { get; set; }
  public string DbDatabase { get; set; } = string.Empty;
  public string DbUser     { get; set; } = string.Empty;
  public string DbPassword { get; set; } = string.Empty;

  public string SqlText { get; set; } = string.Empty;
}