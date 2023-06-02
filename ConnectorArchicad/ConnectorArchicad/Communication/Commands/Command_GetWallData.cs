using System.Collections.Generic;
using System.Threading.Tasks;
using Speckle.Core.Kits;
using Speckle.Newtonsoft.Json;
using Objects.BuiltElements.Archicad;
using Speckle.Core.Logging;

namespace Archicad.Communication.Commands
{
  sealed internal class GetWallData : ICommand<IEnumerable<ArchicadWall>>
  {
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class Parameters
    {
      [JsonProperty("applicationIds")]
      private IEnumerable<string> ApplicationIds { get; }

      public Parameters(IEnumerable<string> applicationIds)
      {
        ApplicationIds = applicationIds;
      }
    }

    [JsonObject(MemberSerialization.OptIn)]
    private sealed class Result
    {
      [JsonProperty("walls")]
      public IEnumerable<ArchicadWall> Datas { get; private set; }
    }

    private IEnumerable<string> ApplicationIds { get; }

    public GetWallData(IEnumerable<string> applicationIds)
    {
      ApplicationIds = applicationIds;
    }

    public async Task<IEnumerable<ArchicadWall>> Execute(CumulativeTimer cumulativeTimer)
    {
      Result result = await HttpCommandExecutor.Execute<Parameters, Result>(
        "GetWallData",
        new Parameters(ApplicationIds),
        cumulativeTimer
      );
      foreach (var wall in result.Datas)
        wall.units = Units.Meters;

      return result.Datas;
    }
  }
}
