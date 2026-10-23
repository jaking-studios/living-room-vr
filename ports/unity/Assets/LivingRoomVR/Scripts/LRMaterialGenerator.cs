// Builds every glTF material with the project's own small shaders, so nothing depends on
// glTFast's shader variants being included in a player build.
using GLTFast;
using GLTFast.Logging;
using GLTFast.Materials;
using UnityEngine;

namespace LivingRoomVR
{
    public class LRMaterialGenerator : IMaterialGenerator
    {
        static Shader s_Lit, s_LitT;
        Material m_Default;

        static Shader Lit => s_Lit ? s_Lit : (s_Lit = Shader.Find("LRVR/Lit"));
        static Shader LitT => s_LitT ? s_LitT : (s_LitT = Shader.Find("LRVR/LitTransparent"));

        public Material GetDefaultMaterial(bool pointsSupport = false)
        {
            if (!m_Default) m_Default = new Material(Lit) { name = "default" };
            return m_Default;
        }

        public Material GenerateMaterial(GLTFast.Schema.MaterialBase gltfMaterial, IGltfReadable gltf, bool pointsSupport = false)
        {
            string n = (gltfMaterial.name ?? "").ToLowerInvariant();
            bool glass = n.Contains("glass") || n.Contains("crys") || n.Contains("bot") || n.Contains("sheer");
            bool blend = gltfMaterial.GetAlphaMode() == GLTFast.Schema.MaterialBase.AlphaMode.Blend;
            var m = new Material(blend || glass ? LitT : Lit) { name = gltfMaterial.name };
            if (blend || glass) m.renderQueue = 3000;

            var pbr = gltfMaterial.PbrMetallicRoughness;
            Color col = Color.white;
            if (pbr != null)
            {
                col = pbr.BaseColor; // linear
                var bt = pbr.BaseColorTexture;
                if (bt != null && bt.index >= 0) { var t = gltf.GetTexture(bt.index); if (t) m.SetTexture("_MainTex", t); }
            }
            if (glass && col.a > 0.5f) col.a = n.Contains("sheer") ? 0.55f : 0.22f;
            // glTF factors are linear; Material.SetColor expects sRGB in a linear-colour project
            m.SetColor("_Color", col.gamma);

            Color em = gltfMaterial.Emissive;
            // glTFast does not read KHR_materials_emissive_strength, so give glowing parts a fixed boost
            m.SetColor("_EmissionColor", (em * 2.5f).gamma);
            var et = gltfMaterial.EmissiveTexture;
            if (et != null && et.index >= 0) { var t = gltf.GetTexture(et.index); if (t) m.SetTexture("_EmissionMap", t); }
            return m;
        }

        public void SetLogger(ICodeLogger logger) { }
    }
}
