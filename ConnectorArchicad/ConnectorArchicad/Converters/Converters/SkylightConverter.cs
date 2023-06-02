using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Archicad.Communication;
using Archicad.Model;
using Speckle.Core.Models;
using Speckle.Core.Models.GraphTraversal;
using Speckle.Core.Logging;

namespace Archicad.Converters
{
  public sealed class Skylight : IConverter
  {
    public Type Type => typeof(Objects.BuiltElements.Archicad.ArchicadSkylight);

    public async Task<List<ApplicationObject>> ConvertToArchicad(IEnumerable<TraversalContext> elements, CumulativeTimer cumulativeTimer, CancellationToken token)
    {
      var skylights = new List<Objects.BuiltElements.Archicad.ArchicadSkylight>();

      using (cumulativeTimer.Begin(ConnectorArchicad.Properties.OperationNameTemplates.ConvertToNative, Type.Name))
      {
        foreach (var tc in elements)
        {
          switch (tc.current)
          {
            case Objects.BuiltElements.Archicad.ArchicadSkylight archicadSkylight:
              archicadSkylight.parentApplicationId = tc.parent.current.id;
              skylights.Add(archicadSkylight);
              break;
              //case Objects.BuiltElements.Opening skylight:
              //  var baseLine = (Line)wall.baseLine;
              //  var newWall = new Objects.BuiltElements.Archicad.ArchicadDoor(Utils.ScaleToNative(baseLine.start),
              //    Utils.ScaleToNative(baseLine.end), Utils.ScaleToNative(wall.height, wall.units));
              //  if (el is RevitWall revitWall)
              //    newWall.flipped = revitWall.flipped;
              //  walls.Add(newWall);
              //  break;
          }
        }
      }

      var result = await AsyncCommandProcessor.Execute(new Communication.Commands.CreateSkylight(skylights), token, cumulativeTimer);

      return result is null ? new List<ApplicationObject>() : result.ToList();
    }

    public async Task<List<Base>> ConvertToSpeckle(IEnumerable<Model.ElementModelData> elements, CumulativeTimer cumulativeTimer, CancellationToken token)
    {
      // Get subelements
      var elementModels = elements as ElementModelData[] ?? elements.ToArray();
      IEnumerable<Objects.BuiltElements.Archicad.ArchicadSkylight> datas =
        await AsyncCommandProcessor.Execute(new Communication.Commands.GetSkylightData(elementModels.Select(e => e.applicationId)), cumulativeTimer);

      if (datas is null)
      {
        return new List<Base>();
      }

      List<Base> openings = new List<Base>();
      foreach (Objects.BuiltElements.Archicad.ArchicadSkylight subelement in datas)
      {
        subelement.displayValue =
          Operations.ModelConverter.MeshesToSpeckle(elementModels.First(e => e.applicationId == subelement.applicationId)
            .model);
        openings.Add(subelement);
      }

      return openings;
    }
  }
}
