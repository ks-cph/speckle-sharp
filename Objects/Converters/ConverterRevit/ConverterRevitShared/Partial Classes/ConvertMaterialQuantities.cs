using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Objects.BuiltElements;
using Objects.BuiltElements.Revit;
using Speckle.Core.Models;
using System.Collections.Generic;
using System.Linq;
using DB = Autodesk.Revit.DB;

namespace Objects.Converter.Revit
{
  public partial class ConverterRevit
  {
    #region MaterialQuantity
    /// <summary>
    /// Gets the quantitiy of a material in one element
    /// </summary>
    /// <param name="element"></param>
    /// <param name="material"></param>
    /// <returns></returns>
    public Objects.Other.MaterialQuantity MaterialQuantityToSpeckle(DB.Element element, DB.Material material, string units)
    {
      if (material == null || element == null) 
        return null;
      double volume = double.NaN;
      double area=double.NaN;

      if(element is DB.Plumbing.Pipe || element is DB.Plumbing.FlexPipe)
      {
        // Area and Volume are computed based on geometry elements
        GetGeometry(element, out List<DB.Mesh> meshes, out List<DB.Solid> solids);
        volume = solids.Sum(s => s.Volume);
        area = solids.Select(s => s.Faces.Cast<DB.Face>().Select(face => face.Area).Max()).Sum();
      }
      else if (RequiresGeometryComputation(element))
      {
        // Area and Volume are computed based on geometry elements
        GetGeometry(element, out List<DB.Mesh> meshes, out List<DB.Solid> solids);
        var filteredSolids = solids.Where(solid => solid.Volume > 0 && !solid.Faces.IsEmpty && solid.Faces.get_Item(0).MaterialElementId == material.Id);
        volume = filteredSolids.Sum(solid => solid.Volume);
        area  = filteredSolids
          .Select(solid => solid.Faces.Cast<Face>().Select(face => face.Area)
          .Max()).Sum();
      }
      else
      {
        // To-Do: These methods from the Revit API appear to have bugs.
        volume += element.GetMaterialVolume(material.Id);
        area += element.GetMaterialArea(material.Id, false);
      }
      // Convert revit interal units to speckle commit units
      double factor = ScaleToSpeckle(1);
      volume *= factor * factor * factor;
      area *= factor * factor;

      var speckleMaterial = ConvertAndCacheMaterial(material.Id, material.Document);
      var materialQuantity = new Objects.Other.MaterialQuantity(speckleMaterial, volume, area, units);

      if (LocationToSpeckle(element) is ICurve curve)
        materialQuantity["length"] = curve.length;
      else if (element is DB.Architecture.Railing)
        materialQuantity["length"] = (element as DB.Architecture.Railing).GetPath().Sum(e => e.Length)*factor;
      else if (element is DB.Architecture.TopRail)
        materialQuantity["length"] = (element as DB.Architecture.TopRail).GetPath().Sum(e => e.Length) * factor;
      return materialQuantity;
    }
    #endregion
    #region MaterialQuantities
    public IEnumerable<Objects.Other.MaterialQuantity> MaterialQuantitiesToSpeckle(DB.Element element, string units)
    {
      var matIDs = element?.GetMaterialIds(false); 
      // Does not return the correct materials for some categories
      // Need to take different approach for MEP-Elements
      if (matIDs == null || !matIDs.Any() &&  element is MEPCurve)
      {
        DB.Material mepMaterial = ConverterRevit.GetMEPSystemRevitMaterial(element);
        if (mepMaterial != null) matIDs.Add(mepMaterial.Id);
      }
      else  if (matIDs == null || !matIDs.Any() && (element is DB.Architecture.Railing|| element is DB.Architecture.TopRail))
      {
        matIDs = GetMaterialsFromGeometry(element).ToList();
      }
      if (matIDs == null || !matIDs.Any())
        return null;
      var materials = matIDs.Select(material => element.Document.GetElement(material) as DB.Material);
      return MaterialQuantitiesToSpeckle(element, materials, units);
    }
    public IEnumerable<Objects.Other.MaterialQuantity> MaterialQuantitiesToSpeckle(DB.Element element, IEnumerable<DB.Material> materials, string units)
    {
      if (materials == null || !materials.Any()) return null;
      List<Objects.Other.MaterialQuantity> quantities = new List<Objects.Other.MaterialQuantity>();

      foreach (var material in materials)
        quantities.Add(MaterialQuantityToSpeckle(element, material, units));

      return quantities;
    }
    #endregion
    /// <summary>
    /// Revit API default methods for computing volumes and areas is malfunctioning for some objects
    /// 
    /// </summary>
    /// <param name="element"></param>
    /// <returns>true f the element's quantities needs to be computetd based on the geometry.returns>
    private bool RequiresGeometryComputation(DB.Element element)
    {
      if (element.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Windows || 
        element.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Doors ||
        element is DB.Architecture.Railing||
        element is DB.Architecture.TopRail) return true;
      return false;
    }

    private IEnumerable<DB.ElementId> GetMaterialsFromGeometry(DB.Element element, bool isConverterAsInstance=false, DB.Options options=null)
    {
      GetGeometry(element, out List<DB.Mesh> meshes, out List<DB.Solid> solids);
      HashSet<ElementId> materialIds = new HashSet<ElementId>();

      return solids.Where(solid => solid.Volume > 0 && !solid.Faces.IsEmpty).Select(m => m.Faces.get_Item(0).MaterialElementId).Distinct();
    }

    private void GetGeometry(DB.Element element, out List<DB.Mesh> meshes, out List<DB.Solid> solids, bool isConvertedAsInstance = false, DB.Options options = null)
    {
      options ??= new DB.Options();
      GeometryElement geom = null;
      solids = null;
      meshes = null;
      try
      {
        geom = element.get_Geometry(options);
      }
      catch (Autodesk.Revit.Exceptions.ArgumentException)
      {
        options.ComputeReferences = false;
        geom = element.get_Geometry(options);
      }
      if (geom != null)
      {
        // retrieves all meshes and solids from a geometry element
        List<DB.Solid> sol = new List<DB.Solid>();
        List<DB.Mesh> meshs = new List<DB.Mesh>();
        SortGeometry(geom);
        void SortGeometry(GeometryElement geom)
        {
          foreach (GeometryObject geomObj in geom)
          {
            switch (geomObj)
            {
              case DB.Solid solid:
                if (solid.Faces.Size > 0 && System.Math.Abs(solid.SurfaceArea) > 0) // skip invalid solid
                  sol.Add(solid);
                break;
              case DB.Mesh mesh:
                meshs.Add(mesh);
                break;
              case GeometryInstance instance:
                var instanceGeo = isConvertedAsInstance ? instance.GetSymbolGeometry() : instance.GetInstanceGeometry();
                SortGeometry(instanceGeo);
                break;
              case GeometryElement element:
                SortGeometry(element);
                break;
            }
          }
        }
        solids = sol;
        meshes = meshs;
      }
    }
  }
}
