using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using Newtonsoft.Json;
using WaymarkPresetPlugin.Resources;
using WaymarkPresetPlugin.Windows.Map;

namespace WaymarkPresetPlugin.Windows.Library;

public class LibraryWindow : Window, IDisposable
{
    private readonly Plugin Plugin;

    private string PresetImportString = "";
    public int SelectedPreset { get; private set; } = -1;
    private uint GameSlotDropdownSelection = 1;
    private bool FieldMarkerAddonWasOpen { get; set; }

    private int? LibraryZoneDragAndDrop;
    private int? LibraryPresetDragAndDrop;

    private ZoneSearcher LibraryWindowZoneSearcher { get; set; } = new();
    private string SearchText = "";

    private bool FieldMarkerAddonVisible;
    private Vector2 DockedPosition = Vector2.Zero;

    private const string ZoneSortDataFileNameV1 = "LibraryZoneSortData_v1.json";

    public Vector2 WindowSize = Vector2.Zero;
    public Vector2 WindowPosition = Vector2.Zero;

    public LibraryWindow(Plugin plugin) : base("Waymark Library###WaymarkLibrary")
    {
        Plugin = plugin;

        PositionCondition = ImGuiCond.Always;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 375),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        ReadZoneFile();
    }

    public void Dispose()
    {
        //	Try to save off the zone sort data if we have any.
        WriteZoneSortDataToFile();
    }

    public override void PreOpenCheck()
    {
        //	Handle game window docking stuff.
        DockedPosition = Vector2.Zero;
        FieldMarkerAddonVisible = false;
        unsafe
        {
            var pFieldMarkerAddon = (AtkUnitBase*) Plugin.GameGui.GetAddonByName("FieldMarker");
            if (pFieldMarkerAddon != null && pFieldMarkerAddon->IsVisible && pFieldMarkerAddon->RootNode != null)
            {
                FieldMarkerAddonVisible = true;
                DockedPosition.X = pFieldMarkerAddon->X + pFieldMarkerAddon->RootNode->Width * pFieldMarkerAddon->Scale;
                DockedPosition.Y = pFieldMarkerAddon->Y;
            }
        }

        IsOpen = Plugin.Configuration.OpenAndCloseWithFieldMarkerAddon switch
        {
            true when FieldMarkerAddonWasOpen && !FieldMarkerAddonVisible => false,
            true when !FieldMarkerAddonWasOpen && FieldMarkerAddonVisible => true,
            _ => IsOpen
        };

        FieldMarkerAddonWasOpen = FieldMarkerAddonVisible;
    }

    public override void Draw()
    {
        //	Draw the window.
        if (Plugin.Configuration.AttachLibraryToFieldMarkerAddon && FieldMarkerAddonVisible)
            Position = DockedPosition;

        WindowSize = ImGui.GetWindowSize();
        WindowPosition = ImGui.GetWindowPos();

        // Position change has applied, so we set it to null again
        if (WindowPosition == Position)
            Position = null;

        var style = ImGui.GetStyle();

        if (ImGui.Checkbox(Language.ConfigOptionFilteronCurrentZone, ref Plugin.Configuration.mFilterOnCurrentZone))
            Plugin.Configuration.Save();

        var saveCurrentWaymarksButtonText = Language.ButtonSaveCurrentWaymarks;
        ImGui.SameLine(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(saveCurrentWaymarksButtonText).X - ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetStyle().WindowPadding.X);
        if (ImGui.Button(saveCurrentWaymarksButtonText))
        {
            FieldMarkerPreset currentWaymarks = new();
            if (MemoryHandler.GetCurrentWaymarksAsPresetData(ref currentWaymarks))
                if (Plugin.Configuration.PresetLibrary.ImportPreset(currentWaymarks) >= 0)
                    Plugin.Configuration.Save();
        }
        else
        {
            using var _ = ImRaii.PushColor(ImGuiCol.Button, style.Colors[(int)ImGuiCol.Button] * 0.5f)
                                .Push(ImGuiCol.ButtonHovered, style.Colors[(int)ImGuiCol.Button])
                                .Push(ImGuiCol.ButtonActive, style.Colors[(int)ImGuiCol.Button])
                                .Push(ImGuiCol.Text, style.Colors[(int)ImGuiCol.TextDisabled]);
            ImGui.Button(saveCurrentWaymarksButtonText);
        }

        // The string to use for filtering the list of zones
        var zoneFilterString = "";

        // Show the search text box when not filtering on current zone
        if (Plugin.Configuration.ShowLibraryZoneFilterBox && !Plugin.Configuration.FilterOnCurrentZone && Plugin.Configuration.SortPresetsByZone)
        {
            using var pushedWidth = ImRaii.ItemWidth(ImGui.CalcTextSize("_").X * 20u);
            ImGui.InputText(Language.LibraryWindowTextZoneSearchLabel, ref SearchText, 16u);

            zoneFilterString = SearchText;
        }

        (int, int, bool)? presetDragDropResult = null;
        (ushort, ushort)? zoneDragDropResult = null;
        using (var child = ImRaii.Child("Library Preset List Child Window"))
        {
            if (child.Success)
            {
                using (ImRaii.Group())
                {
                    if (Plugin.Configuration.PresetLibrary.Presets.Count > 0)
                    {
                        if (Plugin.Configuration.mSortPresetsByZone)
                        {
                            var anyPresetsVisibleWithCurrentFilters = false;
                            var dict = Plugin.Configuration.PresetLibrary.GetSortedIndices(ZoneSortType.Custom, Plugin.Configuration.SortZonesDescending);
                            foreach (var zone in dict)
                            {
                                if (Plugin.Configuration.FilterOnCurrentZone && zone.Key != ZoneInfoHandler.GetContentFinderIDFromTerritoryTypeID(Plugin.ClientState.TerritoryType))
                                    continue;

                                var zoneInfo = ZoneInfoHandler.GetZoneInfoFromContentFinderID(zone.Key);
                                if (IsZoneFilteredBySearch(zoneFilterString, zoneInfo))
                                {
                                    anyPresetsVisibleWithCurrentFilters = true;
                                    if (ImGui.CollapsingHeader(zoneInfo.DutyName))
                                    {
                                        var tempZoneResult = DoZoneDragAndDrop(zoneInfo);
                                        var tempPresetResult = DrawPresetsForZone(zone);
                                        zoneDragDropResult ??= tempZoneResult;
                                        presetDragDropResult ??= tempPresetResult;
                                    }
                                    else
                                    {
                                        var tempZoneResult = DoZoneDragAndDrop(zoneInfo);
                                        zoneDragDropResult ??= tempZoneResult;
                                    }
                                }
                            }

                            if (!Plugin.Configuration.SortZonesDescending)
                            {
                                var tempZoneResult = DrawZoneDragDropTopOrBottomPlaceholder(false);
                                zoneDragDropResult ??= tempZoneResult;
                            }

                            if (!anyPresetsVisibleWithCurrentFilters)
                                ImGui.TextUnformatted(Language.MainWindowTextNoPresetsFound);
                        }
                        else
                        {
                            if (ImGui.CollapsingHeader(Language.HeaderPresets))
                                presetDragDropResult = DrawUncategorizedPresets();
                        }
                    }
                    else
                    {
                        ImGui.TextUnformatted(Language.MainWindowTextLibraryEmpty);
                    }
                }

                ImGuiHelpers.ScaledDummy(5.0f);

                if (ImGui.CollapsingHeader(Language.HeaderImportOptions))
                    DrawImportSection();

                if (ImGui.CollapsingHeader(Language.HeaderExportandBackupOptions))
                    DrawExportSection();
            }
        }

        //	Handle moving a zone header if the user wanted to.
        if (zoneDragDropResult != null)
        {
            //	If it's the first time someone is dragging and dropping, set the sort order to what's currently visible.
            if (Plugin.Configuration.PresetLibrary.GetCustomSortOrder().Count == 0)
            {
                List<ushort> baseSortOrder = [];
                foreach (var zone in Plugin.Configuration.PresetLibrary.GetSortedIndices(ZoneSortType.Custom))
                    baseSortOrder.Add(zone.Key);

                Plugin.Configuration.PresetLibrary.SetCustomSortOrder(baseSortOrder, Plugin.Configuration.SortZonesDescending);
                Plugin.Log.Debug("Tried to set up initial zone sort order.");
            }

            //	Modify the sort entry for the drag and drop.
            Plugin.Configuration.PresetLibrary.AddOrChangeCustomSortEntry(zoneDragDropResult.Value.Item1, zoneDragDropResult.Value.Item2);
            Plugin.Log.Debug($"Tried to move zone id {zoneDragDropResult.Value.Item1} in front of {zoneDragDropResult.Value.Item2}.");
        }

        //	Handle moving a preset now if the user wanted to.
        if (presetDragDropResult == null)
            return;

        Plugin.InfoPaneWindow.IsOpen = true;
        SelectedPreset = Plugin.Configuration.PresetLibrary.MovePreset(presetDragDropResult.Value.Item1, presetDragDropResult.Value.Item2, presetDragDropResult.Value.Item3);
        if (SelectedPreset == -1)
        {
            Plugin.Log.Debug($"Unable to move preset {presetDragDropResult.Value.Item1} to {(presetDragDropResult.Value.Item3 ? "after " : "")}index {presetDragDropResult.Value.Item2}.");
        }
        else
        {
            Plugin.Log.Debug($"Moved preset {presetDragDropResult.Value.Item1} to index {SelectedPreset}.");
            Plugin.Configuration.Save();
        }

        LibraryPresetDragAndDrop = null;
    }

    private unsafe (int, int, bool)? DrawPresetsForZone(KeyValuePair<ushort, List<int>> zonePresets)
    {
        var doDragAndDropMove = false;
        var indexToMove = -1;
        var indexToMoveTo = -1;
        var moveToAfter = false;
        var indices = zonePresets.Value;
        for (var i = 0; i < indices.Count; ++i)
        {
            if (ImGui.Selectable($"{Plugin.Configuration.PresetLibrary.Presets[indices[i]].Name}{(Plugin.Configuration.ShowLibraryIndexInPresetInfo ? $" ({indices[i]})" : "")}###_Preset_{indices[i]}", indices[i] == SelectedPreset, ImGuiSelectableFlags.AllowDoubleClick))
            {
                //	It's probably a bad idea to allow the selection to change when a preset's being edited.
                if (!Plugin.EditorWindow.EditingPreset)
                {
                    if (Plugin.Configuration.AllowUnselectPreset && indices[i] == SelectedPreset && !ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                    {
                        SelectedPreset = -1;
                    }
                    else
                    {
                        SelectedPreset = indices[i];
                        Plugin.InfoPaneWindow.IsOpen = true;
                    }

                    Plugin.InfoPaneWindow.CancelPendingDelete();
                }

                //	Place preset when its entry in the library window is double clicked.
                if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    var preset = Plugin.Configuration.PresetLibrary.Presets[indices[i]].GetAsGamePreset();
                    MemoryHandler.PlacePreset(preset);
                }
            }

            if (!Plugin.EditorWindow.EditingPreset && Plugin.Configuration.AllowPresetDragAndDropOrdering)
            {
                using var source = ImRaii.DragDropSource(ImGuiDragDropFlags.SourceNoHoldToOpenOthers);
                if (source.Success)
                {
                    ImGui.SetDragDropPayload($"PresetIdxZ{zonePresets.Key}", nint.Zero, 0);
                    LibraryPresetDragAndDrop = indices[i];

                    ImGui.TextUnformatted($"{Language.DragandDropPreviewMovingPreset} {Plugin.Configuration.PresetLibrary.Presets[indices[i]].Name}{(Plugin.Configuration.ShowLibraryIndexInPresetInfo ? $" ({indices[i]})" : "")}");
                }
            }

            if (!Plugin.EditorWindow.EditingPreset && Plugin.Configuration.AllowPresetDragAndDropOrdering)
            {
                using var target = ImRaii.DragDropTarget();
                if (!target.Success)
                    continue;

                var payload = ImGui.AcceptDragDropPayload($"PresetIdxZ{zonePresets.Key}", ImGuiDragDropFlags.AcceptBeforeDelivery | ImGuiDragDropFlags.AcceptNoDrawDefaultRect);
                if (payload.NativePtr != null && LibraryPresetDragAndDrop.HasValue)
                {
                    if (payload.IsDelivery())
                    {
                        indexToMove = LibraryPresetDragAndDrop.Value;
                        indexToMoveTo = indices[i];
                        doDragAndDropMove = true;

                        LibraryPresetDragAndDrop = null;
                    }
                    else
                    {
                        Helper.AddOverline(new Vector4(1.0f, 1.0f, 0.0f, 1.0f), 3.0f);
                    }
                }
            }
        }

        if (!Plugin.EditorWindow.EditingPreset && Plugin.Configuration.AllowPresetDragAndDropOrdering)
        {
            if (ImGui.GetDragDropPayload().NativePtr != null && LibraryPresetDragAndDrop is >= 0 && LibraryPresetDragAndDrop < Plugin.Configuration.PresetLibrary.Presets.Count && Plugin.Configuration.PresetLibrary.Presets[LibraryPresetDragAndDrop.Value].MapID == zonePresets.Key)
            {
                ImGui.Selectable(Language.DragandDropPreviewMovetoBottom);

                using var target = ImRaii.DragDropTarget();
                if (target.Success)
                {
                    var payload = ImGui.AcceptDragDropPayload($"PresetIdxZ{zonePresets.Key}", ImGuiDragDropFlags.AcceptBeforeDelivery | ImGuiDragDropFlags.AcceptNoDrawDefaultRect);
                    if (payload.NativePtr != null && LibraryPresetDragAndDrop.HasValue)
                    {
                        if (payload.IsDelivery())
                        {
                            indexToMove = LibraryPresetDragAndDrop.Value;
                            indexToMoveTo = indices.Last();
                            moveToAfter = true;
                            doDragAndDropMove = true;

                            LibraryPresetDragAndDrop = null;
                        }
                        else
                        {
                            Helper.AddOverline(new Vector4(1.0f, 1.0f, 0.0f, 1.0f), 3.0f);
                        }
                    }
                }
            }
        }

        //	Return the drag and drop results.
        return doDragAndDropMove ? new ValueTuple<int, int, bool>(indexToMove, indexToMoveTo, moveToAfter) : null;
    }

    private unsafe (int, int, bool)? DrawUncategorizedPresets()
    {
        var doDragAndDropMove = false;
        var indexToMove = -1;
        var indexToMoveTo = -1;
        var anyPresetsVisibleWithCurrentFilters = false;
        for (var i = 0; i < Plugin.Configuration.PresetLibrary.Presets.Count; ++i)
        {
            if (!Plugin.Configuration.FilterOnCurrentZone || Plugin.Configuration.PresetLibrary.Presets[i].MapID == ZoneInfoHandler.GetContentFinderIDFromTerritoryTypeID(Plugin.ClientState.TerritoryType))
            {
                anyPresetsVisibleWithCurrentFilters = true;
                if (ImGui.Selectable($"{Plugin.Configuration.PresetLibrary.Presets[i].Name}{(Plugin.Configuration.ShowLibraryIndexInPresetInfo ? " (" + i.ToString() + ")" : "")}###_Preset_{i}", i == SelectedPreset, ImGuiSelectableFlags.AllowDoubleClick))
                {
                    //	It's probably a bad idea to allow the selection to change when a preset's being edited.
                    if (!Plugin.EditorWindow.EditingPreset)
                    {
                        if (Plugin.Configuration.AllowUnselectPreset && i == SelectedPreset && !ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                        {
                            SelectedPreset = -1;
                        }
                        else
                        {
                            SelectedPreset = i;
                            Plugin.InfoPaneWindow.IsOpen = true;
                        }

                        Plugin.InfoPaneWindow.CancelPendingDelete();
                    }

                    //	Place preset when its entry in the library window is double clicked.
                    if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                    {
                        var preset = Plugin.Configuration.PresetLibrary.Presets[i].GetAsGamePreset();
                        MemoryHandler.PlacePreset(preset);
                    }
                }

                if (!Plugin.EditorWindow.EditingPreset && Plugin.Configuration.AllowPresetDragAndDropOrdering)
                {
                    using var source = ImRaii.DragDropSource(ImGuiDragDropFlags.SourceNoHoldToOpenOthers);
                    if (source.Success)
                    {
                        ImGui.SetDragDropPayload("PresetIdxAnyZone", nint.Zero, 0);
                        LibraryPresetDragAndDrop = i;

                        ImGui.TextUnformatted($"{Language.DragandDropPreviewMovingPreset} {Plugin.Configuration.PresetLibrary.Presets[i].Name}{(Plugin.Configuration.ShowLibraryIndexInPresetInfo ? $" ({i})" : "")}");
                    }
                }

                if (!Plugin.EditorWindow.EditingPreset && Plugin.Configuration.AllowPresetDragAndDropOrdering)
                {
                    using var target = ImRaii.DragDropTarget();
                    if (target.Success)
                    {
                        var payload = ImGui.AcceptDragDropPayload("PresetIdxAnyZone", ImGuiDragDropFlags.AcceptBeforeDelivery | ImGuiDragDropFlags.AcceptNoDrawDefaultRect);
                        if (payload.NativePtr != null && LibraryPresetDragAndDrop.HasValue)
                        {
                            if (payload.IsDelivery())
                            {
                                indexToMove = LibraryPresetDragAndDrop.Value;
                                indexToMoveTo = i;
                                doDragAndDropMove = true;

                                LibraryPresetDragAndDrop = null;
                            }
                            else
                            {
                                Helper.AddOverline(new Vector4(1.0f, 1.0f, 0.0f, 1.0f), 3.0f);
                            }
                        }
                    }
                }
            }
        }

        if (!Plugin.EditorWindow.EditingPreset && Plugin.Configuration.AllowPresetDragAndDropOrdering)
        {
            if (ImGui.GetDragDropPayload().NativePtr != null && LibraryPresetDragAndDrop is >= 0 && LibraryPresetDragAndDrop < Plugin.Configuration.PresetLibrary.Presets.Count)
            {
                ImGui.Selectable(Language.DragandDropPreviewMovetoBottom);
                using var target = ImRaii.DragDropTarget();
                if (target.Success)
                {
                    var payload = ImGui.AcceptDragDropPayload("PresetIdxAnyZone", ImGuiDragDropFlags.AcceptBeforeDelivery | ImGuiDragDropFlags.AcceptNoDrawDefaultRect);
                    if (payload.NativePtr != null && LibraryPresetDragAndDrop.HasValue)
                    {
                        if (payload.IsDelivery())
                        {
                            indexToMove = LibraryPresetDragAndDrop.Value;
                            indexToMoveTo = Plugin.Configuration.PresetLibrary.Presets.Count;
                            doDragAndDropMove = true;
                        }
                        else
                        {
                            Helper.AddOverline(new Vector4(1.0f, 1.0f, 0.0f, 1.0f), 3.0f);
                        }
                    }
                }
            }
        }

        if (!anyPresetsVisibleWithCurrentFilters)
            ImGui.TextUnformatted(Language.MainWindowTextNoPresetsFound);

        //	Return the drag and drop results.
        return doDragAndDropMove ? new ValueTuple<int, int, bool>(indexToMove, indexToMoveTo, false) : null;
    }

    private unsafe (ushort, ushort)? DoZoneDragAndDrop(ZoneInfo zoneInfo)
    {
        var doDragAndDropMove = false;
        ushort zoneIndexToMove = 0;
        ushort zoneIndexToMoveTo = 0;
        if (!Plugin.EditorWindow.EditingPreset && Plugin.Configuration.AllowZoneDragAndDropOrdering)
        {
            using var source = ImRaii.DragDropSource(ImGuiDragDropFlags.None);
            if (source.Success)
            {
                ImGui.SetDragDropPayload("PresetZoneHeader", nint.Zero, 0);
                LibraryZoneDragAndDrop = zoneInfo.ContentFinderConditionID;

                ImGui.TextUnformatted($"{Language.DragandDropPreviewMovingZone} {zoneInfo.DutyName}");
            }
        }

        if (!Plugin.EditorWindow.EditingPreset && Plugin.Configuration.AllowZoneDragAndDropOrdering)
        {
            using var target = ImRaii.DragDropTarget();
            if (target.Success)
            {
                var payload = ImGui.AcceptDragDropPayload("PresetZoneHeader", ImGuiDragDropFlags.AcceptBeforeDelivery | ImGuiDragDropFlags.AcceptNoDrawDefaultRect);
                if (payload.NativePtr != null && LibraryZoneDragAndDrop.HasValue)
                {
                    if (payload.IsDelivery())
                    {
                        zoneIndexToMove = (ushort)LibraryZoneDragAndDrop.Value;
                        zoneIndexToMoveTo = zoneInfo.ContentFinderConditionID;
                        doDragAndDropMove = true;

                        LibraryZoneDragAndDrop = null;
                    }
                    else
                    {
                        if (Plugin.Configuration.SortZonesDescending)
                            Helper.AddUnderline(new Vector4(1.0f, 1.0f, 0.0f, 1.0f), 3.0f);
                        else
                            Helper.AddOverline(new Vector4(1.0f, 1.0f, 0.0f, 1.0f), 3.0f);
                    }
                }
            }
        }

        //	Return the drag and drop results.
        return doDragAndDropMove ? new ValueTuple<ushort, ushort>(zoneIndexToMove, zoneIndexToMoveTo) : null;
    }

    private unsafe (ushort, ushort)? DrawZoneDragDropTopOrBottomPlaceholder(bool isTop)
    {
        var doDragAndDropMove = false;
        ushort zoneIndexToMove = 0;
        ushort zoneIndexToMoveTo = 0;
        if (!Plugin.EditorWindow.EditingPreset && Plugin.Configuration.AllowZoneDragAndDropOrdering)
        {
            if (ImGui.GetDragDropPayload().NativePtr != null && LibraryZoneDragAndDrop is >= 0)
            {
                ImGui.CollapsingHeader(isTop ? Language.DragandDropPreviewMovetoTop : Language.DragandDropPreviewMovetoBottom);
                using var target = ImRaii.DragDropTarget();
                if (target.Success)
                {
                    var payload = ImGui.AcceptDragDropPayload("PresetZoneHeader", ImGuiDragDropFlags.AcceptBeforeDelivery | ImGuiDragDropFlags.AcceptNoDrawDefaultRect);
                    if (payload.NativePtr != null && LibraryZoneDragAndDrop.HasValue)
                    {
                        if (payload.IsDelivery())
                        {
                            zoneIndexToMove = (ushort) LibraryZoneDragAndDrop;
                            zoneIndexToMoveTo = ushort.MaxValue;
                            doDragAndDropMove = true;
                        }
                        else
                        {
                            if (isTop)
                                Helper.AddUnderline(new Vector4(1.0f, 1.0f, 0.0f, 1.0f), 3.0f);
                            else
                                Helper.AddOverline(new Vector4(1.0f, 1.0f, 0.0f, 1.0f), 3.0f);
                        }
                    }
                }
            }
        }

        //	Return the drag and drop results.
        return doDragAndDropMove ? new ValueTuple<ushort, ushort>(zoneIndexToMove, zoneIndexToMoveTo) : null;
    }

    private void DrawImportSection()
    {
        var style = ImGui.GetStyle();
        using var group = ImRaii.Group();
        ImGui.InputTextWithHint("##JSONImportTextBox", Language.TextBoxPromptImport, ref PresetImportString, 1024); //Most exports max out around 500 characters with all waymarks, so this leaves heaps of room for a long name.
        ImGui.SameLine();
        if (ImGui.Button(Language.ButtonImport))
        {
            Plugin.Log.Information($"Attempting to import preset string: {PresetImportString}");
            if (Plugin.Configuration.PresetLibrary.ImportPreset(PresetImportString) >= 0)
            {
                PresetImportString = "";
                Plugin.Configuration.Save();
            }
        }

        if (ImGui.Button(Language.MainWindowTextImportfromGameSlotLabel))
        {
            if (Plugin.Configuration.PresetLibrary.ImportPreset(MemoryHandler.ReadSlot(GameSlotDropdownSelection)) >= 0)
                Plugin.Configuration.Save();
        }

        ImGui.SameLine();

        var comboWidth = ImGui.CalcTextSize($"{MemoryHandler.MaxPresetSlotNum}").X + style.FramePadding.X * 3f + ImGui.GetTextLineHeightWithSpacing();
        ImGui.SetNextItemWidth(comboWidth);
        using (var combo = ImRaii.Combo("###ImportGameSlotNumberDropdown", $"{GameSlotDropdownSelection}"))
        {
            if (combo.Success)
            {
                for (uint i = 1; i <= MemoryHandler.MaxPresetSlotNum; ++i)
                    if (ImGui.Selectable($"{i}", i == GameSlotDropdownSelection))
                        GameSlotDropdownSelection = i;
            }
        }

        try
        {
            using (ImRaii.PushColor(ImGuiCol.Text, style.Colors[(int)ImGuiCol.Button]))
            using (ImRaii.PushFont(UiBuilder.IconFont))
                ImGui.TextUnformatted("\uF0C1");

            ImGui.SameLine();
            Helper.UrlLink("https://github.com/Em-Six/FFXIVWaymarkPresets/wiki", Language.MainWindowTextPresetResourcesLink, false, UiBuilder.IconFont);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Unable to open the requested link");
        }
    }

    private void DrawExportSection()
    {
        using var group = ImRaii.Group();
        if (ImGui.Button(Language.ButtonExportAllPresetstoClipboard))
        {
            try
            {
                var str = Plugin.Configuration.PresetLibrary.Presets.Aggregate("", (current, preset) => $"{current}{WaymarkPresetExport.GetExportString(preset)}\r\n");
                ImGui.SetClipboardText(str);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "Error while exporting all presets");
            }
        }

        if (ImGui.Button(Language.ButtonBackupCurrentConfig))
        {
            Plugin.Configuration.Save();
            WriteZoneSortDataToFile();
            Plugin.MapWindow.WriteMapViewStateToFile();
            Plugin.Configuration.BackupConfigFile();
            Plugin.Configuration.BackupConfigFolderFile(ZoneSortDataFileNameV1[..ZoneSortDataFileNameV1.LastIndexOf('.')], ZoneSortDataFileNameV1[(ZoneSortDataFileNameV1.LastIndexOf('.') + 1)..]);
            Plugin.Configuration.BackupConfigFolderFile(MapWindow.MapViewStateDataFileNameV1[..MapWindow.MapViewStateDataFileNameV1.LastIndexOf('.')], MapWindow.MapViewStateDataFileNameV1[(MapWindow.MapViewStateDataFileNameV1.LastIndexOf('.') + 1)..]);
        }

        ImGuiComponents.HelpMarker(Language.HelpBackupCurrentConfig);
    }

    private void ReadZoneFile()
    {
        //	Try to read in the zone sort data.
        try
        {
            var zoneSortDataFilePath = Path.Join(Plugin.PluginInterface.GetPluginConfigDirectory(), ZoneSortDataFileNameV1);
            if (File.Exists(zoneSortDataFilePath))
            {
                var jsonStr = File.ReadAllText(zoneSortDataFilePath);
                var sortData = JsonConvert.DeserializeObject<List<ushort>>(jsonStr);
                if (sortData != null)
                    Plugin.Configuration.PresetLibrary.SetCustomSortOrder(sortData);
            }
            else
            {
                Plugin.Log.Information("No zone sort order file found; using default sort order.");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Unable to load library zone sort data");
        }
    }

    private void WriteZoneSortDataToFile()
    {
        try
        {
            var sortData = Plugin.Configuration.PresetLibrary.GetCustomSortOrder();
            if (sortData.Count != 0)
            {
                var jsonStr = JsonConvert.SerializeObject(sortData);
                var zoneSortDataFilePath = Path.Join(Plugin.PluginInterface.GetPluginConfigDirectory(), ZoneSortDataFileNameV1);
                File.WriteAllText(zoneSortDataFilePath, jsonStr);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Unable to save library zone sort data");
        }
    }

    internal void ClearAllZoneSortData()
    {
        Plugin.Configuration.PresetLibrary.ClearCustomSortOrder();
        var zoneSortDataFilePath = Path.Join(Plugin.PluginInterface.GetPluginConfigDirectory(), ZoneSortDataFileNameV1);
        if (File.Exists(zoneSortDataFilePath))
            File.Delete(zoneSortDataFilePath);
    }

    public void TrySetSelectedPreset(int presetIndex)
    {
        if (!Plugin.EditorWindow.EditingPreset)
        {
            SelectedPreset = presetIndex;
            Plugin.InfoPaneWindow.IsOpen = true;
        }
    }

    public void TryDeselectPreset()
    {
        if (!Plugin.EditorWindow.EditingPreset)
        {
            Plugin.InfoPaneWindow.IsOpen = false;
            SelectedPreset = -1;
        }
    }

    private bool IsZoneFilteredBySearch(string zoneFilterString, ZoneInfo zoneInfo)
    {
        var matchingZones = LibraryWindowZoneSearcher.GetMatchingZones(zoneFilterString);
        return zoneFilterString.Length == 0 || matchingZones.Any(id => id == zoneInfo.ContentFinderConditionID);
    }
}
