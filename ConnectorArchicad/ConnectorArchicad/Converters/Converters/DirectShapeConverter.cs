using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Archicad.Communication;
using Archicad.Model;
using Archicad.Operations;
using DynamicData;
using Objects.Geometry;
using Speckle.Core.Models;
using Speckle.Core.Models.GraphTraversal;
using Speckle.Core.Logging;

namespace Archicad.Converters
{
  public sealed class DirectShape : IConverter
  {
    #region --- Properties ---

    public Type Type => typeof(Objects.BuiltElements.Archicad.DirectShape);

    #endregion

    #region --- Functions ---

    public async Task<List<ApplicationObject>> ConvertToArchicad(IEnumerable<TraversalContext> elements, CumulativeTimer cumulativeTimer, CancellationToken token)
    {
      var directShapes = new List<Objects.BuiltElements.Archicad.DirectShape>();

      using (cumulativeTimer.Begin(ConnectorArchicad.Properties.OperationNameTemplates.ConvertToNative, Type.Name))
      {
        foreach (var tc in elements)
        {
          switch (tc.current)
          {
            case Objects.BuiltElements.Archicad.DirectShape directShape:
              // get the geometry
              MeshModel meshModel = null;

              {
                List<Mesh> meshes = null;
                var m = directShape["displayValue"] ?? directShape["@displayValue"];
                if (m is List<Mesh>)
                  meshes = (List<Mesh>)m;
                else if (m is List<object>)
                  meshes = ((List<object>)m).Cast<Mesh>().ToList();

                if (meshes == null)
                  continue;

                meshModel = ModelConverter.MeshToNative(meshes, cumulativeTimer);
              }

              directShape["model"] = meshModel;
              directShapes.Add(directShape);
              break;
          }
        }
      }

      IEnumerable<ApplicationObject> result;
      result = await AsyncCommandProcessor.Execute(
        new Communication.Commands.CreateDirectShape(directShapes),
        token, cumulativeTimer
      );
      return result is null ? new List<ApplicationObject>() : result.ToList();
    }

    public Task<List<Base>> ConvertToSpeckle(IEnumerable<Model.ElementModelData> elements, CumulativeTimer cumulativeTimer, CancellationToken token)
    {
      return Task.FromResult(
        new List<Base>(
          elements.Select(
            e =>
              new Objects.BuiltElements.Archicad.DirectShape(e.applicationId, ModelConverter.MeshesToSpeckle(e.model))
          )
        )
      );
    }

    #endregion
  }
}
