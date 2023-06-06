﻿using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Gui;
using DesktopUI2.Models;
using DesktopUI2.Models.Filters;
using DesktopUI2.Models.Settings;
using DesktopUI2.ViewModels;

namespace Speckle.ConnectorNavisworks.Other;

public class SelectionHandler
{
  private readonly ISelectionFilter _filter;
  private readonly HashSet<ModelItem> _uniqueModelItems;
  private readonly bool _fullTreeSetting;
  private readonly ProgressViewModel _progressViewModel;
  public ProgressInvoker ProgressBar;
  private HashSet<ModelItem> _visited;
  private int _descendantProgress;

  public SelectionHandler(StreamState state, ProgressViewModel progressViewModel)
  {
    _progressViewModel = progressViewModel;
    _filter = state.Filter;
    _uniqueModelItems = new HashSet<ModelItem>();
    _fullTreeSetting =
      state.Settings.OfType<CheckBoxSetting>().FirstOrDefault(x => x.Slug == "full-tree")?.IsChecked ?? false;
  }

  public int Count => _uniqueModelItems.Count;
  public IEnumerable<ModelItem> ModelItems => _uniqueModelItems.ToList().AsReadOnly();

  internal void GetFromFilter()
  {
    switch (_filter.Slug)
    {
      case "manual":
        _uniqueModelItems.AddRange(GetObjectsFromSelection());
        break;

      case "sets":
        _uniqueModelItems.AddRange(GetObjectsFromSavedSets());
        break;

      case "clashes":
        // TODO: Implement GetObjectsFromClashResults
        break;

      case "views":
        _uniqueModelItems.AddRange(GetObjectsFromSavedViewpoint());
        break;
    }
  }

  /// <summary>
  /// Retrieves the model items from the selection.
  /// </summary>
  private IEnumerable<ModelItem> GetObjectsFromSelection()
  {
    _uniqueModelItems.Clear();

    // Selections are modelItem pseudo-ids.
    var selection = _filter.Selection;
    var count = selection.Count;
    var progressIncrement = 1.0 / count;

    // Begin the progress sub-operation for getting objects from selection
    ProgressBar.BeginSubOperation(0, "Rolling up the sleeves... Time to handpick your favorite data items!");

    // Iterate over the selection and retrieve the corresponding model items
    for (var i = 0; i < count; i++)
    {
      _progressViewModel.CancellationToken.ThrowIfCancellationRequested();
      ProgressBar.Update(i * progressIncrement);

      var pseudoId = selection[i];
      var element = Element.GetElement(pseudoId);
      _uniqueModelItems.Add(element.ModelItem);
    }

    // End the progress sub-operation
    ProgressBar.EndSubOperation();

    return _uniqueModelItems;
  }

  /// <summary>
  /// Retrieves the model items from the saved viewpoint.
  /// </summary>
  private IEnumerable<ModelItem> GetObjectsFromSavedViewpoint()
  {
    _uniqueModelItems.Clear();

    // Get the selection from the filter
    var selection = _filter.Selection.FirstOrDefault();
    if (string.IsNullOrEmpty(selection))
    {
      return Enumerable.Empty<ModelItem>();
    }

    // Resolve the saved viewpoint based on the selection
    var savedViewpoint = ResolveSavedViewpoint(selection);
    if (savedViewpoint == null || !savedViewpoint.ContainsVisibilityOverrides)
    {
      return Enumerable.Empty<ModelItem>();
    }

    // Get the hidden items from the saved viewpoint and invert their visibility
    var items = savedViewpoint.GetVisibilityOverrides().Hidden;
    items.Invert(Application.ActiveDocument);

    // Add the visible items to the unique model items
    _uniqueModelItems.AddRange(items);

    return _uniqueModelItems;
  }

  /// <summary>
  /// Resolves the SavedViewpoint based on the provided saved view reference.
  /// </summary>
  /// <param name="savedViewReference">The saved view reference to resolve.</param>
  /// <returns>The resolved SavedViewpoint.</returns>
  private SavedViewpoint ResolveSavedViewpoint(string savedViewReference)
  {
    // Get a flattened list of viewpoints and their references
    var flattenedViewpointList = Application.ActiveDocument.SavedViewpoints.RootItem.Children
      .Select(GetViews)
      .Where(x => x != null)
      .SelectMany(node => node.Flatten())
      .Select(node => new { Reference = node?.Reference?.Split(':'), node?.Guid })
      .ToList();

    // Find a match based on the saved view reference
    var viewPointMatch = flattenedViewpointList.FirstOrDefault(
      node =>
        node.Guid.ToString() == savedViewReference
        || (node.Reference?.Length == 2 && node.Reference[1] == savedViewReference)
    );

    // If no match is found, return null; otherwise, resolve the SavedViewpoint
    return viewPointMatch == null ? null : ResolveSavedViewpoint(viewPointMatch, savedViewReference);
  }

  /// <summary>
  /// Resolves the SavedViewpoint based on the provided viewpoint match and saved view reference.
  /// </summary>
  /// <param name="viewpointMatch">The dynamic object representing the viewpoint match.</param>
  /// <param name="savedViewReference">The saved view reference to resolve.</param>
  /// <returns>The resolved SavedViewpoint.</returns>
  private SavedViewpoint ResolveSavedViewpoint(dynamic viewpointMatch, string savedViewReference)
  {
    if (Guid.TryParse(savedViewReference, out var guid))
    {
      // Even though we may have already got a match, that could be to a generic Guid from earlier versions of Navisworks
      if (savedViewReference != Guid.Empty.ToString())
      {
        return (SavedViewpoint)Application.ActiveDocument.SavedViewpoints.ResolveGuid(guid);
      }
    }

    if (viewpointMatch?.Reference is not string[] { Length: 2 } reference)
    {
      return null;
    }

    using var savedRef = new SavedItemReference(reference[0], reference[1]);
    using var resolvedReference = Application.ActiveDocument.ResolveReference(savedRef);
    return (SavedViewpoint)resolvedReference;
  }

  /// <summary>
  /// Retrieves the TreeNode representing views for a given SavedItem.
  /// </summary>
  /// <param name="savedItem">The SavedItem for which to retrieve the views.</param>
  /// <returns>The TreeNode representing the views for the given SavedItem.</returns>
  private TreeNode GetViews(SavedItem savedItem)
  {
    // Create a reference to the SavedItem
    var reference = Application.ActiveDocument.SavedViewpoints.CreateReference(savedItem);

    // Create a new TreeNode with properties based on the SavedItem
    var treeNode = new TreeNode
    {
      DisplayName = savedItem.DisplayName,
      Guid = savedItem.Guid,
      IndexWith = nameof(TreeNode.Reference),
      // Rather than version check Navisworks host application we feature check
      // to see if Guid is set correctly on viewpoints.
      Reference = savedItem.Guid.ToString() == Guid.Empty.ToString() ? reference.SavedItemId : savedItem.Guid.ToString()
    };

    // Handle different cases based on whether the SavedItem is a group or not
    switch (savedItem)
    {
      case SavedViewpoint { ContainsVisibilityOverrides: false }:
        // TODO: Determine whether to return null or an empty TreeNode or based on current visibility
        return null;
      case GroupItem groupItem:
        foreach (var childItem in groupItem.Children)
        {
          treeNode.IsEnabled = false;
          treeNode.Elements.Add(GetViews(childItem));
        }
        break;
    }

    // Return the treeNode
    return treeNode;
  }

  /// <summary>
  /// Retrieves the model items from the saved sets.
  /// </summary>
  private IEnumerable<ModelItem> GetObjectsFromSavedSets()
  {
    _uniqueModelItems.Clear();

    // Saved Sets filter stores Guids of the selection sets. This can be converted to ModelItem pseudoIds
    var selections = _filter.Selection.Select(guid => new Guid(guid)).ToList();
    var savedItems = selections.Select(Application.ActiveDocument.SelectionSets.ResolveGuid).OfType<SelectionSet>();

    foreach (var item in savedItems)
    {
      if (item.HasExplicitModelItems)
      {
        _uniqueModelItems.AddRange(item.ExplicitModelItems);
      }
      else if (item.HasSearch)
      {
        _uniqueModelItems.AddRange(item.Search.FindAll(Application.ActiveDocument, false));
      }
    }

    return _uniqueModelItems;
  }

  /// <summary>
  /// Populates the hierarchy by adding ancestor and descendant items to the unique model items.
  /// The unique model items have already been processed to validate that they are not hidden.
  /// </summary>
  public void PopulateHierarchyAndOmitHidden()
  {
    // Check if _uniqueModelItems is null or empty
    if (_uniqueModelItems == null || !_uniqueModelItems.Any())
      return;

    var itemsToPopulate = new HashSet<ModelItem>();
    var itemsToOmit = new HashSet<ModelItem>();
    var totalItems = _uniqueModelItems.Count;

    int updateInterval;

    var startNodes = _uniqueModelItems.ToList();
    
    if (_fullTreeSetting)
    {
      var allAncestors = startNodes.SelectMany(e => e.Ancestors).Distinct().ToList();

      ProgressLooper(
        allAncestors.Count,
        "Brb, time traveling to find your data's great-grandparents...",
        i =>
        {
          _uniqueModelItems.Add(allAncestors.ElementAt(i));
          return true;
        }
      );
    }
    
    _visited = new HashSet<ModelItem>();
    _descendantProgress = 0;
    var allDescendants = startNodes.SelectMany(e => e.Descendants).Distinct().Count();

    foreach (var node in startNodes)
    {
      TraverseDescendants(node, allDescendants);
    }
  }

  private void TraverseDescendants(ModelItem startNode, int totalDescendants)
  {
    var descendantIncrement = 1 / (double)totalDescendants;
    var validDescendants = new HashSet<ModelItem>();

    Stack<ModelItem> stack = new();
    stack.Push(startNode);

    while (stack.Count > 0)
    {
      ModelItem currentNode = stack.Pop();

      if (_visited.Contains(currentNode))
        continue;
      _visited.Add(currentNode);

      if (currentNode.IsHidden)
      {
        var descendantsCount = currentNode.Descendants.Count();
        _descendantProgress += descendantsCount + 1;
      }
      else
      {
        validDescendants.Add(currentNode);
        _descendantProgress++;
      }

      if (currentNode.Children.Any())
      {
        foreach (var child in currentNode.Children.Where(e=>!e.IsHidden))
        {
          stack.Push(child);
        }
      }

      _uniqueModelItems.AddRange(validDescendants);

      if (_descendantProgress % descendantIncrement != 0)
        continue;

      double progress = _descendantProgress / (double)totalDescendants;
      ProgressBar.Update(progress);
    }
  }

  void ProgressLooper(int totalCount, string operationName, Func<int, bool> fn)
  {
    var increment = 1.0 / totalCount;
    var updateInterval = Math.Max(totalCount / 100, 1);
    ProgressBar.BeginSubOperation(0, operationName);
    ProgressBar.Update(0);

    for (int i = 0; i < totalCount; i++)
    {
      _progressViewModel.CancellationToken.ThrowIfCancellationRequested();

      bool shouldContinue = fn(i);

      if (!shouldContinue)
        break;

      if (i % updateInterval != 0)
        continue;

      double progress = (i + 1) * increment;
      ProgressBar.Update(progress);
    }

    ProgressBar.EndSubOperation();
  }

  /// <summary>
  /// Omits items that are hidden from the starting list of nodes if they are not visible in the model.
  /// </summary>
  public void ValidateStartNodes()
  {
    // Remove any nodes that are descendants of hidden nodes.
    _uniqueModelItems.RemoveWhere(e => e.AncestorsAndSelf.Any(a => a.IsHidden));
  }
}
