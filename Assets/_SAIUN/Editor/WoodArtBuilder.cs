using System.IO;
using UnityEditor;
using UnityEngine;

namespace _SAIUN.Editor
{
    /// <summary>
    /// 바닷바람에 바랜 나무 텍스처를 코드로 만든다 (2026-09-29, 사용자 선택 "바닷가 나무 데크" + "바랜 나무").
    /// 화분 판자와 데크 널빤지가 한 장을 같이 쓴다. 외부 그림이 없어 라이선스 걱정이 없다.
    ///  - 세로로 판자 네 줄(줄마다 바램·색조가 다르다), 가로(U)로 결이 흐르고 가로로 이음매 없이 반복된다.
    ///  - 결: 길게 흐르는 섬유 줄무늬, 군데군데 비치는 나이테, 결을 따라 갈라진 틈, 판자에 하나쯤 있는 옹이.
    ///  - 판자 가장자리는 틈이라 어둡고, 바로 안쪽은 닳아 조금 밝다.
    ///  - 노멀 맵은 같은 높이(섬유·틈·가장자리)에서 만든다.
    /// 메시 쪽(FlowerbedMeshes)은 U를 결 방향 길이로, V를 판자 줄(0~3)과 줄 안 위치로 맞춘다.
    /// 파일이 이미 있으면 건드리지 않는다(다시 그리려면 지운다).
    /// </summary>
    public static class WoodArtBuilder
    {
        private const string TextureFolder = "Assets/_SAIUN/Art/Textures";
        public const string AlbedoPath = TextureFolder + "/wood_weathered.png";
        public const string NormalPath = TextureFolder + "/wood_weathered_normal.png";

        private const int Size = 512;
        private const int Rows = 4;

        // ---- 빛깔(선형): 나무 속살의 갈색과 볕에 바랜 은회색 ----
        private static readonly Color Brown = new Color(0.29f, 0.245f, 0.2f);
        private static readonly Color Silver = new Color(0.42f, 0.415f, 0.4f);
        private static readonly Vector3 WarmShift = new Vector3(0.06f, 0.02f, -0.05f);

        // ---- 바램 ----
        private const int WeatherPeriod = 3;
        private const float WeatherAlongBoard = 1.5f;
        private const float WeatherGain = 0.9f;
        private const float WeatherBias = 0.12f;
        private const float RowToneGain = 0.5f;

        // ---- 결 ----
        private const int WarpPeriod = 4;
        private const float WarpAcross = 2.5f;
        private const float RingsPerBoard = 5f;
        private const float RingWarp = 3.5f;
        private const float RingSharpness = 10f;
        private const int RingMaskPeriod = 6;
        private const float RingShade = 0.16f;
        private const int StreakPeriod = 6;
        private const float StreakAcross = 55f;
        private const float StreakShade = 0.22f;
        private const int FiberPeriod = 32;
        private const float FiberAcross = 180f;
        private const float FiberShade = 0.1f;

        // ---- 틈·옹이 ----
        private const int CrackPeriod = 5;
        private const float CrackWidth = 0.006f;
        private const float CrackShade = 0.55f;
        private const float KnotChance = 0.7f;
        private const float KnotStretch = 9f;      // 옹이는 결 방향으로 길쭉하다
        private const float KnotSize = 0.08f;
        private const float KnotShade = 0.45f;
        private const float KnotRingShade = 0.1f;

        // ---- 판자 가장자리 ----
        private const float GrooveWidth = 0.035f;
        private const float GrooveFloor = 0.25f;
        private const float BevelAt = 0.05f;
        private const float BevelLight = 0.06f;

        // ---- 요철 ----
        private const float NormalStrength = 3f;

        // 셰이더에서 흔히 쓰는 사인 해시 계수
        private const float HashX = 127.1f;
        private const float HashY = 311.7f;
        private const float HashSalt = 74.7f;
        private const float HashScale = 43758.5453f;

        /// <summary>바랜 나무 기본 맵과 노멀 맵을 만들고 돌려준다.</summary>
        public static (Texture2D albedo, Texture2D normal) EnsureWoodTextures()
        {
            if (!File.Exists(AlbedoPath) || !File.Exists(NormalPath))
            {
                Directory.CreateDirectory(TextureFolder);
                var colors = new Color32[Size * Size];
                var heights = new float[Size * Size];
                for (int y = 0; y < Size; y++)
                {
                    for (int x = 0; x < Size; x++)
                    {
                        float u = (x + 0.5f) / Size;
                        float v = (y + 0.5f) / Size;
                        Color color = Draw(u, v, out float height);
                        colors[y * Size + x] = color;
                        heights[y * Size + x] = height;
                    }
                }
                WritePng(AlbedoPath, colors, linear: false);
                WritePng(NormalPath, NormalsFromHeights(heights), linear: true);
                AssetDatabase.ImportAsset(AlbedoPath, ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(NormalPath, ImportAssetOptions.ForceSynchronousImport);
            }

            Configure(AlbedoPath, TextureImporterType.Default, sRgb: true);
            Configure(NormalPath, TextureImporterType.NormalMap, sRgb: false);
            return (AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath), AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath));
        }

        // 한 화소: u는 결 방향(주기 1), v는 판자 네 줄.
        private static Color Draw(float u, float v, out float height)
        {
            int row = Mathf.FloorToInt(v * Rows);
            float t = v * Rows - row;

            float rowTone = Hash(row, row * 3, 11) - 0.5f;
            float rowWarm = Hash(row, row * 5, 13) - 0.5f;
            float weather = Mathf.Clamp01(Fbm(u * WeatherPeriod, row * 7.3f + t * WeatherAlongBoard, WeatherPeriod, 21) * WeatherGain
                                          + WeatherBias + rowTone * RowToneGain);
            Color wood = Color.Lerp(Brown, Silver, weather);
            wood.r *= 1f + rowWarm * WarmShift.x;
            wood.g *= 1f + rowWarm * WarmShift.y;
            wood.b *= 1f + rowWarm * WarmShift.z;

            // 나이테 결: 판자를 따라 길게 흐르고 조금씩 휜다. 군데군데만 비친다.
            float warp = Fbm(u * WarpPeriod, t * WarpAcross + row * 5.1f, WarpPeriod, 31);
            float rings = Mathf.Abs(Mathf.Sin(Mathf.PI * (t * RingsPerBoard + warp * RingWarp + row * 0.37f)));
            float ringMask = Mathf.Clamp01(Fbm(u * RingMaskPeriod, t * 3f + row * 2.3f, RingMaskPeriod, 33) * 2.2f - 0.7f);
            float ringLine = Mathf.Pow(rings, RingSharpness) * ringMask;
            float streak = PeriodicNoise(u * StreakPeriod, t * StreakAcross + row * 40f, StreakPeriod, 41);
            float fiber = PeriodicNoise(u * FiberPeriod, t * FiberAcross + row * 90f, FiberPeriod, 43);
            float shade = 1f - RingShade * ringLine + StreakShade * (streak - 0.5f) + FiberShade * (fiber - 0.5f);

            // 결을 따라 갈라진 틈
            float crackAt = Hash(row, row * 7, 51) * 0.6f + 0.2f;
            float crackMask = Mathf.Clamp01((Fbm(u * CrackPeriod, row * 9.1f, CrackPeriod, 53) - 0.55f) * 6f);
            float crack = Mathf.Exp(-Mathf.Abs(t - crackAt - (warp - 0.5f) * 0.08f) / CrackWidth) * crackMask;
            shade *= 1f - CrackShade * crack;

            // 옹이: 판자에 하나쯤, 결 방향으로 길쭉하다.
            float knot = 0f;
            if (Hash(row, row * 17, 67) < KnotChance)
            {
                float du = u - Hash(row, row * 11, 61);
                du -= Mathf.Round(du);
                float kt = Hash(row, row * 13, 63) * 0.5f + 0.25f;
                float distance = Mathf.Sqrt(du * KnotStretch * du * KnotStretch + (t - kt) * 1.3f * (t - kt) * 1.3f);
                knot = Mathf.Exp(-distance / KnotSize);
                float ring = Mathf.Pow(Mathf.Abs(Mathf.Sin(distance * 40f)), 6f) * Mathf.Exp(-distance / 0.25f);
                shade *= 1f - KnotShade * knot - KnotRingShade * ring;
            }

            // 판자 가장자리: 틈은 어둡고, 바로 안쪽 모서리는 닳아 조금 밝다.
            float edge = Mathf.Min(t, 1f - t);
            float groove = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(edge / GrooveWidth));
            float bevel = Mathf.Exp(-(edge - BevelAt) * (edge - BevelAt) / 0.0004f) * BevelLight;
            shade = shade * (GrooveFloor + (1f - GrooveFloor) * groove) + bevel;

            height = ringLine * 0.3f + (streak - 0.5f) * 0.4f + (fiber - 0.5f) * 0.2f - crack * 0.8f - (1f - groove) + knot * 0.2f;
            Color color = wood * shade;
            color.a = 1f;
            return color.gamma;
        }

        // 이웃 높이 차로 기울기를 재 탄젠트 공간 법선으로 바꾼다. 가로는 반대편에서 읽어 이음매가 없다.
        private static Color32[] NormalsFromHeights(float[] heights)
        {
            var normals = new Color32[heights.Length];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float left = heights[y * Size + (x + Size - 1) % Size];
                    float right = heights[y * Size + (x + 1) % Size];
                    float down = heights[Mathf.Max(y - 1, 0) * Size + x];
                    float up = heights[Mathf.Min(y + 1, Size - 1) * Size + x];
                    var normal = new Vector3((left - right) * NormalStrength, (down - up) * NormalStrength, 1f).normalized;
                    normals[y * Size + x] = new Color(normal.x * 0.5f + 0.5f, normal.y * 0.5f + 0.5f, normal.z * 0.5f + 0.5f, 1f);
                }
            }
            return normals;
        }

        // 가로로만 주기가 있는 값 노이즈를 옥타브마다 두 배 잘게 겹친다(0~1).
        private static float Fbm(float x, float y, int period, int salt)
        {
            float sum = 0f;
            float amplitude = 0.5f;
            float total = 0f;
            int scale = 1;
            for (int octave = 0; octave < 3; octave++)
            {
                sum += PeriodicNoise(x * scale, y * scale, period * scale, salt + octave) * amplitude;
                total += amplitude;
                amplitude *= 0.5f;
                scale *= 2;
            }
            return sum / total;
        }

        // 가로(x) 주기 period의 값 노이즈(0~1). 세로는 주기가 없다.
        private static float PeriodicNoise(float x, float y, int period, int salt)
        {
            int ix = Mathf.FloorToInt(x);
            int iy = Mathf.FloorToInt(y);
            float fx = x - ix;
            float fy = y - iy;
            float sx = fx * fx * (3f - 2f * fx);
            float sy = fy * fy * (3f - 2f * fy);
            float a = Hash(Wrap(ix, period), iy, salt);
            float b = Hash(Wrap(ix + 1, period), iy, salt);
            float c = Hash(Wrap(ix, period), iy + 1, salt);
            float d = Hash(Wrap(ix + 1, period), iy + 1, salt);
            return Mathf.Lerp(Mathf.Lerp(a, b, sx), Mathf.Lerp(c, d, sx), sy);
        }

        private static int Wrap(int value, int period) => ((value % period) + period) % period;

        private static float Hash(float x, float y, int salt)
        {
            float n = Mathf.Sin(x * HashX + y * HashY + salt * HashSalt) * HashScale;
            return n - Mathf.Floor(n);
        }

        private static void WritePng(string path, Color32[] pixels, bool linear)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false, linear);
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }

        private static void Configure(string path, TextureImporterType type, bool sRgb)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) return;
            if (importer.textureType == type && importer.sRGBTexture == sRgb && importer.wrapMode == TextureWrapMode.Repeat) return;
            importer.textureType = type;
            importer.sRGBTexture = sRgb;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.SaveAndReimport();
        }
    }
}
