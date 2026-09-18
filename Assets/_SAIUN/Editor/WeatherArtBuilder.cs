using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace _SAIUN.Editor
{
    /// <summary>
    /// 자연 현상(P2-05)의 임시 그림을 코드로 만든다. 뭉게구름 스프라이트 3종과 빗줄기 재질.
    /// 외부 그림을 쓰지 않아 라이선스 걱정이 없다. 파일이 이미 있으면 건드리지 않는다.
    /// </summary>
    public static class WeatherArtBuilder
    {
        private const string TextureFolder = "Assets/_SAIUN/Art/Textures";
        private const string RainMaterialPath = "Assets/_SAIUN/Art/Materials/Particle_Rain.mat";
        private const string ParticleShaderName = "Universal Render Pipeline/Particles/Unlit";

        private const int CloudWidth = 256;
        private const int CloudHeight = 128;
        private const float CloudBottom = 22f;      // 뭉게구름의 평평한 밑면 높이(픽셀)
        private const float EdgeSoftness = 5f;      // 가장자리가 흐려지는 폭(픽셀)
        private const float BottomShade = 0.84f;    // 밑면 밝기. 위로 갈수록 1이 되어 부피감이 난다.
        private const float ShadeHeight = 70f;

        private static readonly Color RainColor = new Color(1f, 1f, 1f, 0.42f);

        // 구름마다 겹치는 원(중심 x, 중심 y, 반지름, 픽셀). 모양만 다르고 만드는 법은 같다.
        private static readonly Vector3[][] CloudShapes =
        {
            new[] { new Vector3(66f, 44f, 30f), new Vector3(112f, 62f, 42f), new Vector3(160f, 56f, 38f), new Vector3(200f, 42f, 26f), new Vector3(128f, 38f, 34f) },
            new[] { new Vector3(56f, 40f, 24f), new Vector3(96f, 58f, 36f), new Vector3(146f, 70f, 46f), new Vector3(196f, 50f, 32f), new Vector3(122f, 36f, 32f) },
            new[] { new Vector3(80f, 46f, 34f), new Vector3(128f, 60f, 40f), new Vector3(176f, 46f, 30f), new Vector3(128f, 36f, 30f) },
        };

        /// <summary>구름 스프라이트를 만들고 돌려준다.</summary>
        public static Sprite[] EnsureCloudSprites()
        {
            var sprites = new Sprite[CloudShapes.Length];
            for (int i = 0; i < CloudShapes.Length; i++)
            {
                string path = $"{TextureFolder}/cloud_{i}.png";
                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(TextureFolder);
                    File.WriteAllBytes(path, DrawCloud(CloudShapes[i]));
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                }

                if (AssetImporter.GetAtPath(path) is TextureImporter importer
                    && (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single))
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.alphaIsTransparency = true;
                    importer.mipmapEnabled = false;
                    importer.SaveAndReimport();
                }
                sprites[i] = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            }
            return sprites;
        }

        /// <summary>빗줄기용 반투명 재질. URP 머티리얼 인스펙터가 알파 블렌드를 고를 때 넣는 값을 직접 넣는다.</summary>
        public static Material EnsureRainMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(RainMaterialPath);
            if (material != null) return material;

            Shader shader = Shader.Find(ParticleShaderName);
            if (shader == null)
            {
                Debug.LogError("WeatherArtBuilder: URP 파티클 셰이더를 찾지 못했습니다.");
                return null;
            }

            material = new Material(shader) { name = Path.GetFileNameWithoutExtension(RainMaterialPath) };
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
            material.SetColor("_BaseColor", RainColor);

            var texture = AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd");
            if (texture != null) material.SetTexture("_BaseMap", texture);

            AssetDatabase.CreateAsset(material, RainMaterialPath);
            return material;
        }

        // 원 여러 개의 합집합을 평평한 밑면으로 자르고, 가장자리는 부드럽게, 밑은 살짝 어둡게 칠한다.
        private static byte[] DrawCloud(Vector3[] circles)
        {
            var texture = new Texture2D(CloudWidth, CloudHeight, TextureFormat.RGBA32, false);
            var pixels = new Color32[CloudWidth * CloudHeight];
            for (int y = 0; y < CloudHeight; y++)
            {
                for (int x = 0; x < CloudWidth; x++)
                {
                    var point = new Vector2(x + 0.5f, y + 0.5f);
                    float distance = float.MaxValue;
                    foreach (Vector3 circle in circles)
                    {
                        distance = Mathf.Min(distance, Vector2.Distance(point, circle) - circle.z);
                    }
                    distance = Mathf.Max(distance, CloudBottom - point.y);

                    float alpha = 1f - Mathf.SmoothStep(0f, 1f, (distance + EdgeSoftness) / (EdgeSoftness * 2f));
                    float shade = Mathf.Lerp(BottomShade, 1f, Mathf.Clamp01((point.y - CloudBottom) / ShadeHeight));
                    byte value = (byte)Mathf.RoundToInt(shade * 255f);
                    pixels[y * CloudWidth + x] = new Color32(value, value, value, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            byte[] png = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);
            return png;
        }
    }
}
