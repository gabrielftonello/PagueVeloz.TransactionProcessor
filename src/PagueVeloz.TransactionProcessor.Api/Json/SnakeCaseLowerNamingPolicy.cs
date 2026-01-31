using System.Text;
using System.Text.Json;

namespace PagueVeloz.TransactionProcessor.Api.Json;

public sealed class SnakeCaseLowerNamingPolicy : JsonNamingPolicy
{
  public override string ConvertName(string name)
  {
    if (string.IsNullOrEmpty(name))
      return name;

    var sb = new StringBuilder(name.Length + 8);
    for (var i = 0; i < name.Length; i++)
    {
      var c = name[i];
      if (char.IsUpper(c))
      {
        if (i > 0)
          sb.Append('_');
        sb.Append(char.ToLowerInvariant(c));
      }
      else
      {
        sb.Append(c);
      }
    }
    return sb.ToString();
  }
}
