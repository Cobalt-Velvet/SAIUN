using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace _SAIUN.Editor
{
    /// <summary>
    /// 자연 현상(P2-05)의 그림을 코드로 만든다. 구름 셰이더 재질, 빗줄기·바람결 재질, 날리는 잎 텍스처와 재질.
    /// 외부 그림을 쓰지 않아 라이선스 걱정이 없다. 파일이 이미 있으면 건드리지 않는다.
    /// </summary>
    public static class WeatherArtBuilder
    {
        private const string TextureFolder = "Assets/_SAIUN/Art/Textures";
        private const string RainMaterialPath = "Assets/_SAIUN/Art/Materials/Particle_Rain.mat";
        private const string LeafMaterialPath = "Assets/_SAIUN/Art/Materials/Particle_Leaf.mat";
        private const string WindMaterialPath = "Assets/_SAIUN/Art/Materials/Particle_Wind.mat";
        private const string CloudMaterialPath = "Assets/_SAIUN/Art/Materials/UI_Cumulus.mat";
        private const string LeafTexturePath = TextureFolder + "/leaf.png";
        private const string CloudShaderName = "SAIUN/UI/Cumulus";
        private const int LeafSize = 32;
        private const string ParticleShaderName = "Universal Render Pipeline/Particles/Unlit";

        // 잎 텍스처 모양(텍스처 폭에 대한 비율)
        private const float LeafHalfWidth = 0.36f;    // 가장 넓은 곳의 반폭
        private const float LeafTipTaper = 0.6f;      // 끝으로 갈수록 좁아지는 정도(1이면 대칭)
        private const float LeafEdgeSharpness = 0.8f; // 가장자리 경계의 선명도
        private const float VeinHalfWidth = 0.03f;
        private const float VeinShade = 0.85f;

        private static readonly Color RainColor = new Color(1f, 1f, 1f, 0.42f);

        /// <summary>빗줄기·빗방울 튐용 반투명 재질.</summary>
        public static Material EnsureRainMaterial()
        {
            return EnsureAlphaParticleMaterial(RainMaterialPath, RainColor,
                AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd"));
        }

        /// <summary>바람결 꼬리용 반투명 재질. 둥근 알갱이 텍스처가 꼬리 길이로 늘어나 양 끝과 가장자리가 부드럽다.</summary>
        public static Material EnsureWindMaterial()
        {
            return EnsureAlphaParticleMaterial(WindMaterialPath, Color.white,
                AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd"));
        }

        /// <summary>날리는 잎 재질. 잎 모양 텍스처에 파티클 색을 곱한다.</summary>
        public static Material EnsureLeafMaterial()
        {
            return EnsureAlphaParticleMaterial(LeafMaterialPath, Color.white, EnsureLeafTexture());
        }

        /// <summary>뭉게구름 셰이더 재질.</summary>
        public static Material EnsureCloudMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(CloudMaterialPath);
            if (material != null) return material;

            Shader shader = Shader.Find(CloudShaderName);
            if (shader == null)
            {
                Debug.LogError("WeatherArtBuilder: 구름 셰이더를 찾지 못했습니다.");
                return null;
            }
            material = new Material(shader) { name = Path.GetFileNameWithoutExtension(CloudMaterialPath) };
            AssetDatabase.CreateAsset(material, CloudMaterialPath);
            return material;
        }

        // URP 머티리얼 인스펙터가 알파 블렌드를 고를 때 넣는 값을 직접 넣는다.
        private static Material EnsureAlphaParticleMaterial(string path, Color color, Texture2D texture)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            Shader shader = Shader.Find(ParticleShaderName);
            if (shader == null)
            {
                Debug.LogError("WeatherArtBuilder: URP 파티클 셰이더를 찾지 못했습니다.");
                return null;
            }

            material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
            material.SetFloat("_Surface", 1f);   // Transparent
            material.SetFloat("_Blend", 0f);     // Alpha
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetColor("_BaseColor", color);
            if (texture != null) material.SetTexture("_BaseMap", texture);

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        // 끝이 뾰족한 잎사귀. 가운데 잎맥은 살짝 밝게 남긴다. 색은 파티클 색으로 입힌다.
        private static Texture2D EnsureLeafTexture()
        {
            if (!File.Exists(LeafTexturePath))
            {
                Directory.CreateDirectory(TextureFolder);
                var texture = new Texture2D(LeafSize, LeafSize, TextureFormat.RGBA32, false);
                var pixels = new Color32[LeafSize * LeafSize];
                for (int y = 0; y < LeafSize; y++)
                {
                    for (int x = 0; x < LeafSize; x++)
                    {
                        // 잎 길이 방향 t(0~1)에 따라 폭이 sin 모양으로 부풀었다 줄어든다.
                        float t = (y + 0.5f) / LeafSize;
                        float half = Mathf.Sin(t * Mathf.PI) * LeafHalfWidth * Mathf.Lerp(1f, LeafTipTaper, t);
                        float u = (x + 0.5f) / LeafSize - 0.5f;
                        float edge = Mathf.Clamp01((half - Mathf.Abs(u)) * LeafSize * LeafEdgeSharpness);
                        float vein = Mathf.Abs(u) < VeinHalfWidth ? VeinShade : 1f;
                        byte shade = (byte)Mathf.RoundToInt(255f * vein);
                        pixels[y * LeafSize + x] = new Color32(shade, shade, shade, (byte)Mathf.RoundToInt(edge * 255f));
                    }
                }
                texture.SetPixels32(pixels);
                texture.Apply();
                File.WriteAllBytes(LeafTexturePath, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(LeafTexturePath, ImportAssetOptions.ForceSynchronousImport);
                if (AssetImporter.GetAtPath(LeafTexturePath) is TextureImporter importer)
                {
                    importer.alphaIsTransparency = true;
                    importer.mipmapEnabled = false;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.SaveAndReimport();
                }
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(LeafTexturePath);
        }
    }
}
