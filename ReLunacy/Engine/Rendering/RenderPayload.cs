using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReLunacy.Engine.Rendering
{
    /// <summary>
    /// Credits to github.com/RatchetModding/Replanetizer/
    /// </summary>
    public class RenderPayload
    {
        public class VisibilitySettings
        {
            public List<bool> regions = [];
            public List<bool> zones = [];
            public bool renderMobys = true, renderTies = true, renderShrubs = true, renderVolumes = true, renderSkybox = true,
                enableTransparency = true, enableDistanceCulling = false, enableFurstrumCulling = true, showCameras = true,
                showPointLights = true, showSoundSources = true, showGrindRailPaths = true, billboardOnMeshlessModels = true,
                enableAnimations = true;

            public VisibilitySettings() { }
        }

        public Camera camera => Camera.Main;
        public int width;
        public int height;
        public Selection selection;
        public Toolbox? Toolbox; 
        public VisibilitySettings visibility = new();

        public float deltaTime = 1;
        public int forcedAnimationID = 0;

        public RenderPayload(Selection? selec = null, Toolbox? tb = null)
        {
           selection = selec ?? new Selection();
           Toolbox = tb;
        }

        public void SetWindowSize(int w, int h)
        {
            width = w;
            height = h;
        }
    }
}
