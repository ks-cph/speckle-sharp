using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Speckle.Core.Logging;
using Speckle.Core.Models;
using Speckle.Core.Models.GraphTraversal;

namespace Archicad.Converters
{
  public interface IConverter
  {
    #region --- Properties ---

    Type Type { get; }

    #endregion

    #region --- Functions ---

    Task<List<Base>> ConvertToSpeckle(IEnumerable<Model.ElementModelData> elements, CumulativeTimer cumulativeTimer, CancellationToken token);

    Task<List<ApplicationObject>> ConvertToArchicad(IEnumerable<TraversalContext> elements, CumulativeTimer cumulativeTimer, CancellationToken token);

    #endregion
  }
}
