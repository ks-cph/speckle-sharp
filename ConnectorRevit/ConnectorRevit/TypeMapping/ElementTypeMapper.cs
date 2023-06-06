#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using DesktopUI2.Models.Settings;
using DesktopUI2.ViewModels;
using DesktopUI2.Views.Windows.Dialogs;
using RevitSharedResources.Interfaces;
using Speckle.ConnectorRevit.UI;
using Speckle.Core.Kits;
using Speckle.Core.Models;
using Speckle.Newtonsoft.Json;
using DesktopUI2.Models.TypeMappingOnReceive;
using System.Threading.Tasks;
using ReactiveUI;
using Avalonia.Threading;
using ConnectorRevit.TypeMapping;

namespace ConnectorRevit
{
  internal class ElementTypeMapper
  {
    public static async Task Map(ISpeckleConverter converter, ISetting mapOnReceiveSetting, List<ApplicationObject> flattenedCommit, Dictionary<string, Base> storedObjects, Document document)
    {
      if (converter is not IRevitElementTypeRetriever<ElementType, BuiltInCategory> typeRetriever)
      {
        throw new ArgumentException($"Converter does not implement interface {nameof(IRevitElementTypeRetriever<ElementType, BuiltInCategory>)}");
      }

      // Get Settings for recieve on mapping 
      if (mapOnReceiveSetting is not MappingSeting mappingSetting
        || mappingSetting.Selection == null
        || mappingSetting.Selection == ConnectorBindingsRevit.noMapping)
      {
        return;
      }

      var currentMapping = DeserializeMapping(mappingSetting);
      currentMapping ??= new TypeMap();

      var hostTypesContainer = GetHostTypesAndAddIncomingTypes(typeRetriever, flattenedCommit, storedObjects, currentMapping, out var newTypesExist);
      if (!newTypesExist && mappingSetting.Selection != ConnectorBindingsRevit.everyReceive) { return; }

      // show custom mapping dialog if the settings corrospond to what is being received
      var vm = new TypeMappingOnReceiveViewModel(currentMapping, hostTypesContainer, newTypesExist);
      FamilyImporter familyImporter = null;

      currentMapping = await Dispatcher.UIThread.InvokeAsync<ITypeMap>(() => {
        var mappingView = new MappingViewDialog
        {
          DataContext = vm
        };
        return mappingView.ShowDialog<ITypeMap>();
      }).ConfigureAwait(false);

      while (vm.DoneMapping == false)
      {
        familyImporter ??= new FamilyImporter(document);
        await familyImporter.ImportFamilyTypes(hostTypesContainer, typeRetriever).ConfigureAwait(false);
        //hostTypesDict = await ImportFamilyTypes(hostTypesDict).ConfigureAwait(true);

        vm = new TypeMappingOnReceiveViewModel(currentMapping, hostTypesContainer, newTypesExist);
        currentMapping = await Dispatcher.UIThread.InvokeAsync<ITypeMap>(() => {
          var mappingView = new MappingViewDialog
          {
            DataContext = vm
          };
          return mappingView.ShowDialog<ITypeMap>();
        }).ConfigureAwait(false);
      }

      // close the dialog
      MainViewModel.CloseDialog();

      mappingSetting.MappingJson = JsonConvert.SerializeObject(currentMapping);

      // update the mapping object for the user mapped types
      SetMappedValues(typeRetriever, currentMapping);
    }

    private static void SetMappedValues(IRevitElementTypeRetriever<ElementType, BuiltInCategory> typeRetriever, ITypeMap currentMapping)
    {
      foreach (var (@base, mappingValue) in currentMapping.GetAllBasesWithMappings())
      {
        typeRetriever.SetRevitTypeOfBase(@base, mappingValue.OutgoingType ?? mappingValue.InitialGuess);
      }
    }

    public static HostTypeAsStringContainer GetHostTypesAndAddIncomingTypes(IRevitElementTypeRetriever<ElementType, BuiltInCategory> typeRetriever, List<ApplicationObject> flattenedCommit, Dictionary<string, Base> storedObjects, ITypeMap typeMap, out bool newTypesExist)
    {
      var incomingTypes = new Dictionary<string, List<ISingleValueToMap>>();
      var hostTypes = new HostTypeAsStringContainer();

      newTypesExist = false;
      foreach (var appObj in flattenedCommit)
      {
        var @base = storedObjects[appObj.OriginalId];

        var incomingType = typeRetriever.GetRevitTypeOfBase(@base);
        if (incomingType == null) continue; // TODO: do we want to throw an error (or at least log it)

        var typeInfo = typeRetriever.GetRevitTypeInfo(@base);
        var elementTypes = typeRetriever.GetAndCacheAvailibleTypes(typeInfo);
        var exactTypeMatch = typeRetriever.CacheContainsTypeWithName(incomingType);

        if (exactTypeMatch) continue;

        hostTypes.AddCategoryWithTypesIfCategoryIsNew(typeInfo.CategoryName, elementTypes.Select(type => type.Name));
        string initialGuess = DefineInitialGuess(typeMap, incomingType, typeInfo.CategoryName, elementTypes);

        typeMap.AddIncomingType(@base, incomingType, typeInfo.CategoryName, initialGuess, out var isNewType);
        if (isNewType) newTypesExist = true;
      }

      hostTypes.SetAllTypes(
        typeRetriever
          .GetAllCachedElementTypes()
          .Select(type => type.Name)
      );

      return hostTypes;
    }

    private static string DefineInitialGuess(ITypeMap typeMap, string incomingType, string category, IEnumerable<ElementType> elementTypes)
    {
      var existingMappingValue = typeMap.TryGetMappingValueInCategory(category, incomingType);
      string initialGuess;

      if (existingMappingValue != null &&
        (existingMappingValue.InitialGuess != null ||
        existingMappingValue.OutgoingType != null))
      {
        initialGuess = existingMappingValue.OutgoingType ?? existingMappingValue.InitialGuess;
        existingMappingValue.InitialGuess = initialGuess;
        existingMappingValue.OutgoingType = null;
      }
      else
      {
        initialGuess = GetMappedValue(elementTypes, category, incomingType);
      }

      return initialGuess;
    }

    public Dictionary<string, List<MappingValue>>? DeserializeMappingAsDict(MappingSeting mappingSetting)
    {
      if (mappingSetting.MappingJson != null)
      {
        return JsonConvert.DeserializeObject<Dictionary<string, List<MappingValue>>>(mappingSetting.MappingJson);
      }
      return null;
    }
    
    public static ITypeMap? DeserializeMapping(MappingSeting mappingSetting)
    {
      if (mappingSetting.MappingJson != null)
      {
        var settings = new JsonSerializerSettings
        {
          Converters = { new AbstractConverter<MappingValue, ISingleValueToMap>() },
        };
        return JsonConvert.DeserializeObject<TypeMap>(mappingSetting.MappingJson, settings);
      }
      return null;
    }

    /// <summary>
    /// Gets the most similar host type of the same category for a single incoming type
    /// </summary>
    /// <param name="elementTypes"></param>
    /// <param name="category"></param>
    /// <param name="speckleType"></param>
    /// <returns></returns>
    private static string GetMappedValue(IEnumerable<ElementType> elementTypes, string category, string speckleType)
    {
      var shortestDistance = int.MaxValue;
      var closestType = $"No families of the category \"{category}\" are loaded into the project";

      foreach (var elementType in elementTypes)
      {
        var distance = LevenshteinDistance(speckleType, elementType.Name);
        if (distance < int.MaxValue)
        {
          shortestDistance = distance;
          closestType = elementType.Name;
        }
      }

      return closestType;
    }

    /// <summary>
    /// Returns the distance between two strings
    /// </summary>
    /// <param name="s"></param>
    /// <param name="t"></param>
    /// <returns>distance as an integer</returns>
    private static int LevenshteinDistance(string s, string t)
    {
      // Default algorithim for computing the similarity between strings
      int n = s.Length;
      int m = t.Length;
      int[,] d = new int[n + 1, m + 1];
      if (n == 0)
      {
        return m;
      }
      if (m == 0)
      {
        return n;
      }
      for (int i = 0; i <= n; d[i, 0] = i++)
        ;
      for (int j = 0; j <= m; d[0, j] = j++)
        ;
      for (int i = 1; i <= n; i++)
      {
        for (int j = 1; j <= m; j++)
        {
          int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
          d[i, j] = Math.Min(
              Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
              d[i - 1, j - 1] + cost);
        }
      }
      return d[n, m];
    }
  }

  public class AbstractConverter<TReal, TAbstract> : JsonConverter where TReal : TAbstract
  {
    public override bool CanConvert(Type objectType)
        => objectType == typeof(TAbstract);

    public override object ReadJson(JsonReader reader, Type type, Object value, JsonSerializer jser)
        => jser.Deserialize<TReal>(reader);

    public override void WriteJson(JsonWriter writer, Object value, JsonSerializer jser)
        => jser.Serialize(writer, value);
  }
}
