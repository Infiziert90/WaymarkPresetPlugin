﻿using System.Collections.Generic;
using System.Numerics;

namespace WaymarkPresetPlugin.UI;

//	Helper class to store data on how each zones map is being viewed.  Using plain fields instead of properties so that we can easily hold short-term refs to them for aliasing.
internal class MapViewState
{
    public int SelectedSubMapIndex = 0;
    public List<SubMapViewState> SubMapViewData { get; protected set; } = new();

    public class SubMapViewState
    {
        public SubMapViewState(float zoom, Vector2 pan)
        {
            Zoom = zoom;
            Pan = pan;
        }

        //	We want to be able to get these as refs, so no properties.
        public float Zoom;
        public Vector2 Pan;
    }
}