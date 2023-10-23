#nullable enable
using Autodesk.Revit.DB;
using ConverterRevitShared.Extensions;
using Objects.Other;
using System.Collections.Generic;
using System.Linq;
using DB = Autodesk.Revit.DB;

namespace Objects.Converter.Revit
{
  public partial class ConverterRevit
  {
    /// <summary>
    /// Material Quantities in Revit are stored in different ways and therefore need to be retrieved 
    /// using different methods. According to this forum post https://forums.autodesk.com/t5/revit-api-forum/method-getmaterialarea-appears-to-use-different-formulas-for/td-p/11988215
    /// "Hosts" (whatever that means) will return the area of a single side of the object while other
    /// objects will return the combined area of every side of the element. MEP element materials are attached
    /// to the MEP system that the element belongs to.
    /// </summary>
    /// <param name="element"></param>
    /// <param name="units"></param>
    /// <returns></returns>
    public IEnumerable<Other.MaterialQuantity> MaterialQuantitiesToSpeckle(DB.Element element, string units)
    {
      if (MaterialAreaAPICallWillReportSingleFace(element))
      {
        return GetMaterialQuantitiesFromAPICall(element, units);
      }
      else if (element.IsMEPElement())
      {
        MaterialQuantity quantity = GetMaterialQuantityForMEPElement(element, units);
        return quantity == null ? new List<MaterialQuantity>() : new List<MaterialQuantity>() { quantity };
      }
      else
      {
        return GetMaterialQuantitiesFromSolids(element, units);
      }
    }

    private IEnumerable<MaterialQuantity> GetMaterialQuantitiesFromAPICall(DB.Element element, string units)
    {
      foreach (ElementId matId in element.GetMaterialIds(false))
      {
        double volume = element.GetMaterialVolume(matId);
        double area = element.GetMaterialArea(matId, false);
        yield return Create(element, matId, area, volume, units);
      }
    }

    private MaterialQuantity? GetMaterialQuantityForMEPElement(DB.Element element, string units)
    {
      DB.Material material = GetMEPSystemRevitMaterial(element);
      DB.Options options = new() { DetailLevel = ViewDetailLevel.Fine };
      var (solids, _) = GetSolidsAndMeshesFromElement(element, options);
      var (area, volume) = GetAreaAndVolumeFromSolids(solids);
      if (material == null)
      {
        ElementId matId = GetMaterialsFromSolids(solids)?.First();
        return Create(element, matId, area, volume, units);
      }
      else
      {
        return Create(element, material.Id, area, volume, units);
      }
    }

    private IEnumerable<MaterialQuantity> GetMaterialQuantitiesFromSolids(DB.Element element, string units)
    {
      DB.Options options = new() { DetailLevel = ViewDetailLevel.Fine };
      var (solids, _) = GetSolidsAndMeshesFromElement(element, options);

      foreach (ElementId matId in GetMaterialsFromSolids(solids))
      {
        var (area, volume) = GetAreaAndVolumeFromSolids(solids, matId);
        yield return Create(element, matId, area, volume, units);
      }
    }

    private MaterialQuantity Create(
      Element element,
      ElementId materialId,
      double areaRevitInternalUnits,
      double volumeRevitInternalUnits,
      string units)
    {
      Other.Material speckleMaterial = ConvertAndCacheMaterial(materialId, element.Document);
      double factor = ScaleToSpeckle(1);
      double area = factor * factor * areaRevitInternalUnits;
      double volume = factor * factor * factor * volumeRevitInternalUnits;
      MaterialQuantity materialQuantity = new(speckleMaterial, volume, area, units);

      if (LocationToSpeckle(element) is ICurve curve)
      {
        materialQuantity["length"] = curve.length;
      }
      else if (element is DB.Architecture.Railing)
      {
        materialQuantity["length"] = (element as DB.Architecture.Railing).GetPath().Sum(e => e.Length) * factor;
      }
      else if (element is DB.Architecture.ContinuousRail)
      {
        materialQuantity["length"] = (element as DB.Architecture.ContinuousRail).GetPath().Sum(e => e.Length) * factor;
      }

      return materialQuantity;
    }

    private (double, double) GetAreaAndVolumeFromSolids(List<Solid> solids, ElementId? materialId = null)
    {
      if (materialId != null)
      {
        solids = solids
          .Where(
            solid => solid.Volume > 0
            && !solid.Faces.IsEmpty
            && solid.Faces.get_Item(0).MaterialElementId == materialId)
          .ToList();
      }

      double volume = solids.Sum(solid => solid.Volume);
      IEnumerable<double> areaOfLargestFaceInEachSolid = solids
          .Select(solid => solid.Faces.Cast<Face>().Select(face => face.Area)
          .Max());
      double area = areaOfLargestFaceInEachSolid.Sum();
      return (area, volume);
    }

    private IEnumerable<DB.ElementId> GetMaterialsFromSolids(List<Solid> solids)
    {
      return solids
        .Where(solid => solid.Volume > 0 && !solid.Faces.IsEmpty)
        .Select(m => m.Faces.get_Item(0).MaterialElementId)
        .Distinct();
    }

    private bool MaterialAreaAPICallWillReportSingleFace(Element element)
    {
      return element switch
      {
        DB.CeilingAndFloor => true,
        DB.Wall => true,
        DB.RoofBase => true,
        _ => false
      };
    }
    public (List<Solid>, List<DB.Mesh>) GetSolidsAndMeshesFromElement(
      Element element,
      Options options,
      DB.Transform? transform = null
    )
    {
      options = ViewSpecificOptions ?? options ?? new Options();

      GeometryElement geom;
      try
      {
        geom = element.get_Geometry(options);
      }
      catch (Autodesk.Revit.Exceptions.ArgumentException)
      {
        options.ComputeReferences = false;
        geom = element.get_Geometry(options);
      }

      var solids = new List<Solid>();
      var meshes = new List<DB.Mesh>();

      if (geom != null)
      {
        // retrieves all meshes and solids from a geometry element
        SortGeometry(element, solids, meshes, geom, transform?.Inverse);
      }

      return (solids, meshes);
    }
  }
  public static class ElementExtensions
  {
    public static IEnumerable<Connector> GetConnectorSet(this Element element)
    {
      var empty = Enumerable.Empty<Connector>();
      return element.GetConnectorManager()?.Connectors?.Cast<Connector>() ?? empty;
    }

    public static ConnectorManager? GetConnectorManager(this Element element)
    {
      return element switch
      {
        MEPCurve o => o.ConnectorManager,
        FamilyInstance o => o.MEPModel?.ConnectorManager,
        _ => null,
      };
    }

    public static bool IsMEPElement(this Element element)
    {
      return element.GetConnectorManager() != null;
    }
  }


}
