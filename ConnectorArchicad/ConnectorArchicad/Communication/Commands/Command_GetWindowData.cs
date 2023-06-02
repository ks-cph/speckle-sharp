using System.Collections.Generic;
using System.Threading.Tasks;
using Speckle.Core.Kits;
using Speckle.Newtonsoft.Json;
using Objects.BuiltElements.Archicad;
using Speckle.Core.Logging;

namespace Archicad.Communication.Commands
{
  sealed internal class GetWindowData : ICommand<IEnumerable<ArchicadWindow>>
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
      [JsonProperty("windows")]
      public IEnumerable<ArchicadWindow> Datas { get; private set; }
    }

    private IEnumerable<string> ApplicationIds { get; }

    public GetWindowData(IEnumerable<string> applicationIds)
    {
      ApplicationIds = applicationIds;
    }

    public async Task<IEnumerable<ArchicadWindow>> Execute(CumulativeTimer cumulativeTimer)
    {
      Result result = await HttpCommandExecutor.Execute<Parameters, Result>(
        "GetWindowData",
        new Parameters(ApplicationIds),
        cumulativeTimer
      );
      //foreach (var subelement in result.Datas)
      //subelement.units = Units.Meters;

      return result.Datas;
    }
  }
}
