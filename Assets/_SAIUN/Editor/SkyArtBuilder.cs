using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace _SAIUN.Editor
{
    /// <summary>
    /// 하늘(2026-09-28)의 그림 재료를 코드로 만든다. 외부 그림을 쓰지 않는다. 파일이 이미 있으면 건드리지 않는다.
    ///  - 구름 결 노이즈(3D, 64칸 타일): R 값 노이즈 fbm(큰 덩어리·휘기), G·B·A 뒤집은 워리 4·8·16칸(큰·중간·잔 송이).
    ///    워리는 크기마다 따로 둔다. 섞으면 송이가 뭉개진다.
    ///  - 하늘 재질(Hidden/SAIUN/Sky)
    ///  - 시계 글자 그림자 재질: 흰 구름 위를 지나도 밝은 글자가 읽히도록 은은한 그림자(underlay)를 깐다.
    /// </summary>
    public static class SkyArtBuilder
    {
        private const string NoisePath = "Assets/_SAIUN/Art/Textures/SkyNoise.asset";
        private const string MaterialPath = "Assets/_SAIUN/Art/Materials/Sky.mat";
        private const string TextShadowPath = "Assets/_SAIUN/Art/Materials/HUD_TextShadow.mat";
        private const string SkyShaderName = "Hidden/SAIUN/Sky";

        private const int NoiseSize = 64;

        // 값 노이즈 fbm의 칸 수와 무게
        private static readonly int[] ValueCells = { 4, 8, 16, 32 };
        private static readonly float[] ValueWeights = { 0.5f, 0.25f, 0.15f, 0.1f };

        // 워리 채널별 칸 수(G, B, A)
        private static readonly int[] WorleyCells = { 4, 8, 16 };

        // 채널마다 다른 무늬가 되도록 섞는 번호
        private const int ValueSalt = 1;
        private const int WorleySalt = 5;
        private const int PointSaltY = 17;
        private const int PointSaltZ = 31;

        // 정수 해시 계수(시안 페이지와 같은 식)
        private const int PrimeX = 374761393;
        private const int PrimeY = 668265263;
        private const int PrimeZ = 1440662683;
        private const int PrimeSalt = 1274126177;
        private const int ShiftA = 13;
        private const int ShiftB = 16;
        private const float UIntRange = 4294967295f;

        // 시계 글자 그림자: 푸른 기가 도는 짙은 그늘, 아래로 조금, 넓고 부드럽게.
        private static readonly Color TextShadowColor = new Color(0.03f, 0.07f, 0.16f, 0.5f);
        private const float TextShadowOffsetY = -0.35f;
        private const float TextShadowDilate = 0.3f;
        private const float TextShadowSoftness = 0.75f;
        private const string UnderlayKeyword = "UNDERLAY_ON";

        [MenuItem("SAIUN/Rebuild Sky Noise")]
        public static void RebuildNoise()
        {
            AssetDatabase.DeleteAsset(NoisePath);
            EnsureNoise();
        }

        /// <summary>구름 결 노이즈 3D 텍스처.</summary>
        public static Texture3D EnsureNoise()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture3D>(NoisePath);
            if (existing != null) return existing;

            var colors = new Color32[NoiseSize * NoiseSize * NoiseSize];
            int index = 0;
            for (int z = 0; z < NoiseSize; z++)
            for (int y = 0; y < NoiseSize; y++)
            for (int x = 0; x < NoiseSize; x++)
            {
                var p = new Vector3((x + 0.5f) / NoiseSize, (y + 0.5f) / NoiseSize, (z + 0.5f) / NoiseSize);
                float value = 0f;
                for (int o = 0; o < ValueCells.Length; o++) value += ValueNoise(p, ValueCells[o], ValueSalt + o) * ValueWeights[o];
                colors[index++] = new Color32(
                    ToByte(value),
                    ToByte(1f - Worley(p, WorleyCells[0], WorleySalt)),
                    ToByte(1f - Worley(p, WorleyCells[1], WorleySalt + 4)),
                    ToByte(1f - Worley(p, WorleyCells[2], WorleySalt + 8)));
            }

            var texture = new Texture3D(NoiseSize, NoiseSize, NoiseSize, TextureFormat.RGBA32, false)
            {
                name = "SkyNoise",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };
            texture.SetPixels32(colors);
            texture.Apply(false, false);
            Directory.CreateDirectory(Path.GetDirectoryName(NoisePath));
            AssetDatabase.CreateAsset(texture, NoisePath);
            AssetDatabase.SaveAssets();
            return texture;
        }

        /// <summary>하늘 재질. 노이즈를 물려 둔다.</summary>
        public static Material EnsureSkyMaterial()
        {
            Texture3D noise = EnsureNoise();
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                Shader shader = Shader.Find(SkyShaderName);
                if (shader == null)
                {
                    Debug.LogError($"SkyArtBuilder: {SkyShaderName} 셰이더가 없습니다.");
                    return null;
                }
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(MaterialPath) };
                Directory.CreateDirectory(Path.GetDirectoryName(MaterialPath));
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.SetTexture("_Noise", noise);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return material;
        }

        /// <summary>시계 글자 그림자 재질. 글꼴 재질을 복사해 underlay를 켠다.</summary>
        public static Material EnsureTextShadowMaterial(TMP_FontAsset font)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(TextShadowPath);
            if (material != null) return material;
            if (font == null || font.material == null) return null;

            material = new Material(font.material) { name = Path.GetFileNameWithoutExtension(TextShadowPath) };
            material.EnableKeyword(UnderlayKeyword);
            material.SetColor("_UnderlayColor", TextShadowColor);
            material.SetFloat("_UnderlayOffsetY", TextShadowOffsetY);
            material.SetFloat("_UnderlayDilate", TextShadowDilate);
            material.SetFloat("_UnderlaySoftness", TextShadowSoftness);
            Directory.CreateDirectory(Path.GetDirectoryName(TextShadowPath));
            AssetDatabase.CreateAsset(material, TextShadowPath);
            AssetDatabase.SaveAssets();
            return material;
        }

        private static byte ToByte(float value)
        {
            return (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * byte.MaxValue);
        }

        private static float Hash(int x, int y, int z, int salt)
        {
            unchecked
            {
                int h = x * PrimeX + y * PrimeY + z * PrimeZ + salt * PrimeSalt;
                h = (h ^ (int)((uint)h >> ShiftA)) * PrimeSalt;
                h ^= (int)((uint)h >> ShiftB);
                return (uint)h / UIntRange;
            }
        }

        private static int Wrap(int i, int cells)
        {
            return ((i % cells) + cells) % cells;
        }

        // 타일되는 값 노이즈(칸 모서리 값을 부드럽게 잇는다).
        private static float ValueNoise(Vector3 p, int cells, int salt)
        {
            Vector3 s = p * cells;
            int xi = Mathf.FloorToInt(s.x), yi = Mathf.FloorToInt(s.y), zi = Mathf.FloorToInt(s.z);
            float fx = Smooth(s.x - xi), fy = Smooth(s.y - yi), fz = Smooth(s.z - zi);
            float V(int dx, int dy, int dz) => Hash(Wrap(xi + dx, cells), Wrap(yi + dy, cells), Wrap(zi + dz, cells), salt);
            float x00 = Mathf.Lerp(V(0, 0, 0), V(1, 0, 0), fx), x10 = Mathf.Lerp(V(0, 1, 0), V(1, 1, 0), fx);
            float x01 = Mathf.Lerp(V(0, 0, 1), V(1, 0, 1), fx), x11 = Mathf.Lerp(V(0, 1, 1), V(1, 1, 1), fx);
            return Mathf.Lerp(Mathf.Lerp(x00, x10, fy), Mathf.Lerp(x01, x11, fy), fz);
        }

        private static float Smooth(float t)
        {
            return t * t * (3f - 2f * t);
        }

        // 타일되는 워리 노이즈: 가장 가까운 특징점까지의 거리(칸 단위, 0~1로 자름).
        private static float Worley(Vector3 p, int cells, int salt)
        {
            Vector3 s = p * cells;
            int xi = Mathf.FloorToInt(s.x), yi = Mathf.FloorToInt(s.y), zi = Mathf.FloorToInt(s.z);
            float best = float.MaxValue;
            for (int dz = -1; dz <= 1; dz++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int cx = xi + dx, cy = yi + dy, cz = zi + dz;
                int wx = Wrap(cx, cells), wy = Wrap(cy, cells), wz = Wrap(cz, cells);
                var point = new Vector3(cx + Hash(wx, wy, wz, salt), cy + Hash(wx, wy, wz, salt + PointSaltY), cz + Hash(wx, wy, wz, salt + PointSaltZ));
                best = Mathf.Min(best, (point - s).sqrMagnitude);
            }
            return Mathf.Min(1f, Mathf.Sqrt(best));
        }
    }
}
