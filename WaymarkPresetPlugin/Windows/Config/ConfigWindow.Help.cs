using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Numerics;
using WaymarkPresetPlugin.Resources;

namespace WaymarkPresetPlugin.Windows.Config;

public enum HelpWindowPage
{
    General,
    Editing,
    Maps,
    Coordinates,
    CircleCalculator
}

public static class HelpWindowPageExtensions
{
    public static string GetTranslatedName(this HelpWindowPage value)
    {
        return value switch
        {
            HelpWindowPage.General => Language.HeaderHelpWindowPageGeneral,
            HelpWindowPage.Editing => Language.HeaderHelpWindowPageEditing,
            HelpWindowPage.Maps => Language.HeaderHelpWindowPageMaps,
            HelpWindowPage.Coordinates => Language.HeaderHelpWindowPageCoordinates,
            HelpWindowPage.CircleCalculator => Language.HeaderHelpWindowPageCircleCalculator,
            _ => throw new InvalidEnumArgumentException($"Unrecognized HelpWindowPage Enum value {value}."),
        };
    }
}

public partial class ConfigWindow
{
    private Vector3 CircleComputerCenter = Vector3.Zero;
    private float CircleComputerRadiusYalms = 20f;
    private int CircleComputerNumPoints = 8;
    private float CircleComputerAngleOffsetDeg;
    private string CircleComputerMarkerOrder;
    private IDalamudTextureWrap CoordinateSystemsDiagram;
    private HelpWindowPage CurrentHelpPage = HelpWindowPage.General;

    private void InitHelp()
    {
        CoordinateSystemsDiagram = Plugin.Texture.GetFromFile(Path.Join(Plugin.PluginInterface.AssemblyLocation.DirectoryName, "Resources", "CoordinateSystemDiagrams.png")).RentAsync().Result;
    }

    public void Help()
    {
        using var tabItem = ImRaii.TabItem("Help");
        if (!tabItem.Success)
            return;

        var style = ImGui.GetStyle();
        var cachedCurrentHelpPage = CurrentHelpPage;
        for (var i = 0; i < Enum.GetValues<HelpWindowPage>().Length; ++i)
        {
            if (i > 0)
                ImGui.SameLine();

            using var color = ImRaii.PushColor(ImGuiCol.Button, style.Colors[(int)ImGuiCol.ButtonHovered], i == (int)cachedCurrentHelpPage);
            if (ImGui.Button(((HelpWindowPage)i).GetTranslatedName() + $"###HelpButton_{(HelpWindowPage)i}"))
                CurrentHelpPage = (HelpWindowPage)i;
        }

        using var child = ImRaii.Child("###HelpTextPane");
        if (!child.Success)
            return;

        using var wrap = ImRaii.TextWrapPos(ImGui.GetContentRegionAvail().X);
        switch (CurrentHelpPage)
        {
            case HelpWindowPage.General:
                DrawHelpWindow_General();
                break;
            case HelpWindowPage.Editing:
                DrawHelpWindow_Editing();
                break;
            case HelpWindowPage.Maps:
                DrawHelpWindow_Maps();
                break;
            case HelpWindowPage.Coordinates:
                DrawHelpWindow_Coordinates();
                break;
            case HelpWindowPage.CircleCalculator:
                DrawHelpWindow_CircleCalculator();
                break;
            default:
                DrawHelpWindow_General();
                break;
        }
    }

    private void DrawHelpWindow_General()
    {
        ImGui.TextUnformatted(Language.HelpWindowTextGeneral1);
        ImGuiHelpers.ScaledDummy(3.0f);
        ImGui.TextUnformatted(Language.HelpWindowTextGeneral2);
        ImGuiHelpers.ScaledDummy(3.0f);
        ImGui.TextUnformatted(Language.HelpWindowTextGeneral3);
        ImGuiHelpers.ScaledDummy(3.0f);
        ImGui.TextUnformatted(Language.HelpWindowTextGeneral4);
        ImGuiHelpers.ScaledDummy(3.0f);
        ImGui.TextUnformatted(Language.HelpWindowTextGeneral5);
        ImGuiHelpers.ScaledDummy(3.0f);
        ImGui.TextUnformatted(Language.HelpWindowTextGeneral6);
    }

    private void DrawHelpWindow_Editing()
    {
        using (ImRaii.PushColor(ImGuiCol.Text, 0xee4444ff))
            ImGui.TextUnformatted(Language.HelpWindowTextEditingWarningMessage);

        ImGui.Spacing();
        ImGui.TextUnformatted(Language.HelpWindowTextEditing1);
        ImGuiHelpers.ScaledDummy(3.0f);
        ImGui.TextUnformatted(Language.HelpWindowTextEditing2);
    }

    private void DrawHelpWindow_Maps()
    {
        ImGui.TextUnformatted(Language.HelpWindowTextMaps1);
        ImGuiHelpers.ScaledDummy(3.0f);
        ImGui.TextUnformatted(Language.HelpWindowTextMaps2);
        ImGuiHelpers.ScaledDummy(3.0f);
        ImGui.TextUnformatted(Language.HelpWindowTextMaps3);
    }

    private void DrawHelpWindow_Coordinates()
    {
        ImGui.TextUnformatted(Language.HelpWindowTextCoordinates1);
        ImGui.Spacing();
        using (ImRaii.PushIndent())
            ImGui.TextUnformatted(Language.HelpWindowTextCoordinates2);

        ImGuiHelpers.ScaledDummy(3.0f);
        if (CoordinateSystemsDiagram != null)
        {
            const float imgWidthScale = 0.75f;
            const float imguiPaddingScale = 1.0f - imgWidthScale;
            using (ImRaii.PushIndent(ImGui.GetContentRegionAvail().X * imguiPaddingScale / 2f))
            {
                var size = new Vector2(CoordinateSystemsDiagram.Width, CoordinateSystemsDiagram.Height);
                size *= ImGui.GetContentRegionAvail().X / CoordinateSystemsDiagram.Width * imgWidthScale;
                ImGui.Image(CoordinateSystemsDiagram.ImGuiHandle, size);
            }
        }
    }

    private void DrawHelpWindow_CircleCalculator()
    {
        var style = ImGui.GetStyle();
        using (ImRaii.PushColor(ImGuiCol.Text, 0xee4444ff))
            ImGui.TextUnformatted(Language.HelpWindowTextCircleComputerWarningMessage);
        ImGui.Spacing();
        ImGui.TextUnformatted(Language.CircleComputerTextInstructions1);

        ImGuiHelpers.ScaledDummy(5.0f);

        ImGui.InputFloat3(Language.CircleComputerTextCenterPosition, ref CircleComputerCenter);
        ImGui.InputFloat(Language.CircleComputerTextRadius, ref CircleComputerRadiusYalms);
        ImGui.SliderInt(Language.CircleComputerTextNumberofPoints, ref CircleComputerNumPoints, 1, 8);
        ImGui.InputFloat(Language.CircleComputerTextAngleOffset, ref CircleComputerAngleOffsetDeg);
        string[] markerOrderOptions = { "ABCD1234", "A1B2C3D4", "1A2B3C4D" };
        if (CircleComputerNumPoints == 8 && ImGui.BeginCombo(Language.CircleComputerTextMarkerOrder, CircleComputerMarkerOrder))
        {
            foreach (var option in markerOrderOptions)
            {
                bool isSelected = (CircleComputerMarkerOrder == option);
                if (ImGui.Selectable(option, isSelected))
                    CircleComputerMarkerOrder = option;

                if (isSelected)
                    ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }


        ImGuiHelpers.ScaledDummy(5.0f);

        var points = ComputeRadialPositions(CircleComputerCenter, CircleComputerRadiusYalms, CircleComputerNumPoints, CircleComputerAngleOffsetDeg, CircleComputerMarkerOrder);
        for (var i = 0; i < 8; ++i)
        {
            if (i < points.Length)
            {
                ImGui.Selectable($"{i + 1}{points[i].X:F3}, {points[i].Y:F3}, {points[i].Z:F3}");

                using var source = ImRaii.DragDropSource(ImGuiDragDropFlags.None);
                if (source.Success)
                {
                    ImGui.SetDragDropPayload("EditPresetCoords", nint.Zero, 0);
                    Plugin.EditorWindow.WaymarkCoordDragAndDrop = points[i];

                    ImGui.TextUnformatted(Language.DragandDropPreviewCircleComputerWaymark);
                }
            }
            else
            {
                ImGui.TextUnformatted("---");
            }
        }

        ImGuiHelpers.ScaledDummy(5.0f);

        var copyIntoEditorButtonText = Language.ButtonCopyPointsfromCircleComputer;
        if (Plugin.EditorWindow.EditingPreset)
        {
            if (ImGui.Button(copyIntoEditorButtonText + "###Copy these points into the editor button"))
                for (var i = 0; i < points.Length && i < 8; ++i)
                    Plugin.EditorWindow.ScratchEditingPreset.SetWaymark(i, true, points[i]);
        }
        else
        {
            using var color = ImRaii.PushColor(ImGuiCol.Button, style.Colors[(int)ImGuiCol.Button] * 0.5f)
                                    .Push(ImGuiCol.ButtonHovered, style.Colors[(int)ImGuiCol.Button])
                                    .Push(ImGuiCol.ButtonActive, style.Colors[(int)ImGuiCol.Button])
                                    .Push(ImGuiCol.Text, style.Colors[(int)ImGuiCol.TextDisabled]);
            ImGui.Button(copyIntoEditorButtonText + "###Copy these points into the editor button");
        }

        if (ImGui.Button(Language.ButtonCreatePresetfromCircleComputer))
        {
            WaymarkPreset newPreset = new() { Name = Language.DefaultPresetNameCircleComputer };
            for (var i = 0; i < points.Length && i < 8; ++i)
            {
                newPreset[i].Active = true;
                newPreset[i].SetCoords(points[i]);
            }

            var newPresetIndex = Plugin.Configuration.PresetLibrary.ImportPreset(newPreset);
            if (!Plugin.EditorWindow.EditingPreset && newPresetIndex >= 0 && newPresetIndex < Plugin.Configuration.PresetLibrary.Presets.Count)
                Plugin.LibraryWindow.TrySetSelectedPreset(newPresetIndex);
        }
    }

    private Vector3[] ComputeRadialPositions(Vector3 center, float radiusYalms, int numPoints, float angleOffsetDeg = 0f, string? circleComputerMarkerOrder = null)
    {
        numPoints = Math.Max(1, numPoints);
        var computedPoints = new Vector3[numPoints];

        angleOffsetDeg -= 90f;
        var stepAngleDeg = 360.0 / numPoints;

        for (var i = 0; i < numPoints; ++i)
        {
            var angleRad = (i * stepAngleDeg + angleOffsetDeg) * Math.PI / 180.0;
            computedPoints[i].X = (float)Math.Cos(angleRad);
            computedPoints[i].Z = (float)Math.Sin(angleRad);
            computedPoints[i] *= radiusYalms;
            computedPoints[i] += center;
        }

        if (circleComputerMarkerOrder != null && numPoints == 8)
        {
            var orderedPoints = new Vector3[8];
            var markerOrderMap = new Dictionary<char, int>
            {
                { 'A', 0 }, { 'B', 1 }, { 'C', 2 }, { 'D', 3 },
                { '1', 4 }, { '2', 5 }, { '3', 6 }, { '4', 7 }
            };

            for (var i = 0; i < circleComputerMarkerOrder.Length; ++i)
            {
                if (i < numPoints)
                {
                    orderedPoints[markerOrderMap[circleComputerMarkerOrder[i]]] = computedPoints[i];
                }
            }

            return orderedPoints;
        }

        return computedPoints;
    }
}
