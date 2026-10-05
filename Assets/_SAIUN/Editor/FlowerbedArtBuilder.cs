using System.IO;
using UnityEditor;
using UnityEngine;

namespace _SAIUN.Editor
{
    /// <summary>
    /// 화단 흙의 텍스처를 코드로 만든다. 외부 그림이 없어 라이선스 걱정이 없다. 세 장이다.
    ///  - 칸 텍스처(기본 맵): 흙 칸 하나에 한 장이 꼭 맞게 깔린다. 가장자리(고랑)는 어둡고 가운데(둔덕)는 조금 밝아
    ///    칸이 또렷이 읽힌다. 촉촉한 흙의 짙은 밤색(짙은 녹색은 검은 판처럼 보인다).
    ///  - 알갱이 텍스처(디테일 맵, 선형 회색 0.5가 중립): 비틀린 노이즈로 덩이진 명암, 잘고 많은 밝은 부스러기,
    ///    조금 큰 짙은 흙덩이, 드문 옅은 잔돌. 칸과 어긋난 비율로 반복돼 칸마다 같은 무늬가 보이지 않는다.
    ///  - 알갱이 노멀 맵(디테일 노멀).
    /// 알갱이는 크기·길쭉함·방향이 제각각인 타원이라 격자 무늬가 드러나지 않고, 주기 격자 위에서 그려 이음매가 없다.
    /// 파일이 이미 있으면 건드리지 않는다(다시 그리려면 지운다).
    /// </summary>
    public static class FlowerbedArtBuilder
    {
        private const string TextureFolder = "Assets/_SAIUN/Art/Textures";
        public const string SoilCellPath = TextureFolder + "/soil_cell.png";
        public const string SoilDetailPath = TextureFolder + "/soil_detail.png";
        public const string SoilNormalPath = TextureFolder + "/soil_normal.png";

        private const int Size = 256;

        // 촉촉한 밭흙 빛깔(sRGB). 나무 화분·데크와 어울리는 짙은 밤색이다.
        private static readonly Color Earth = new Color32(0x4B, 0x3A, 0x2C, 0xFF);
        private const int CellSize = 128;

        // ---- 칸 텍스처 ----
        private const float GrooveShade = 0.6f;        // 칸 가장자리(고랑) 밝기 배율
        private const float GrooveFade = 0.24f;        // 가장자리에서 이 거리(칸 대비)까지 어둠이 걷힌다
        private const float MoundLight = 1.14f;        // 칸 가운데(둔덕) 밝기 배율
        private const float MoundFade = 0.42f;         // 가운데에서 이 거리(칸 대비)까지 밝음이 걷힌다

        // 디테일 맵은 곱하기 2로 섞인다. 선형 회색 0.5가 원래 색 그대로다.
        private const float DetailNeutral = 0.5f;

        // ---- 덩이진 명암 ----
        private const int ClumpPeriod = 4;             // 한 장에 들어가는 큰 격자 수(정수라야 이음매가 없다)
        private const int ClumpOctaves = 3;
        private const float WarpAmount = 0.8f;         // 격자 무늬를 비트는 정도(격자 칸 단위)
        private const float ClumpDark = 0.74f;         // 오목한 쪽 밝기 배율
        private const float ClumpLight = 1.16f;        // 볼록한 쪽 밝기 배율

        // ---- 알갱이 층 (주기, 칸마다 있을 확률, 반경 범위(칸 대비)) ----
        private const int SpecksPerCell = 2;
        private const float MinAspect = 0.45f;         // 가장 길쭉한 알갱이의 짧은 축 비율
        private const float SoftEdge = 0.7f;           // 이 거리(반경 대비)부터 가장자리가 흐려진다

        private const int CrumbPeriod = 30;
        private const float CrumbChance = 0.45f;
        private const float CrumbRadiusMin = 0.12f;
        private const float CrumbRadiusMax = 0.26f;
        private const float CrumbLight = 1.55f;        // 부스러기 밝기 배율

        private const int ClodPeriod = 13;
        private const float ClodChance = 0.35f;
        private const float ClodRadiusMin = 0.14f;
        private const float ClodRadiusMax = 0.3f;
        private const float ClodShade = 0.62f;         // 흙덩이 밝기 배율

        private const int PebblePeriod = 7;
        private const float PebbleChance = 0.18f;
        private const float PebbleRadiusMin = 0.07f;
        private const float PebbleRadiusMax = 0.12f;
        private const float PebbleLight = 1.9f;        // 잔돌 밝기 배율

        // ---- 요철 ----
        private const float ClumpBump = 0.5f;
        private const float CrumbBump = 0.25f;
        private const float ClodBump = 0.55f;
        private const float PebbleBump = 0.7f;
        private const float NormalStrength = 2.4f;

        // 셰이더에서 흔히 쓰는 사인 해시 계수
        private const float HashX = 127.1f;
        private const float HashY = 311.7f;
        private const float HashSalt = 74.7f;
        private const float HashScale = 43758.5453f;

        // 알갱이 층마다 해시가 겹치지 않게 떨어뜨리는 간격
        private const int SaltStride = 16;
        private const int CrumbSalt = 1;
        private const int ClodSalt = 2;
        private const int PebbleSalt = 3;
        private const int WarpSaltX = 7;
        private const int WarpSaltY = 8;

        /// <summary>칸 텍스처(기본 맵), 알갱이 텍스처(디테일), 알갱이 노멀 맵을 만들고 돌려준다.</summary>
        public static (Texture2D cell, Texture2D detail, Texture2D normal) EnsureSoilTextures()
        {
            if (!File.Exists(SoilCellPath))
            {
                Directory.CreateDirectory(TextureFolder);
                WritePng(SoilCellPath, DrawCell(), CellSize, linear: false);
                AssetDatabase.ImportAsset(SoilCellPath, ImportAssetOptions.ForceSynchronousImport);
            }

            if (!File.Exists(SoilDetailPath) || !File.Exists(SoilNormalPath))
            {
                Directory.CreateDirectory(TextureFolder);
                var shades = new Color32[Size * Size];
                var heights = new float[Size * Size];
                for (int y = 0; y < Size; y++)
                {
                    for (int x = 0; x < Size; x++)
                    {
                        float u = (x + 0.5f) / Size;
                        float v = (y + 0.5f) / Size;

                        // 덩이: 노이즈로 좌표를 한 번 비튼 뒤 다시 노이즈를 읽어 둥글고 불규칙한 얼룩을 만든다.
                        float warpX = (Fbm(u, v, ClumpPeriod, WarpSaltX) - 0.5f) * WarpAmount / ClumpPeriod;
                        float warpY = (Fbm(u, v, ClumpPeriod, WarpSaltY) - 0.5f) * WarpAmount / ClumpPeriod;
                        float clump = Fbm(u + warpX, v + warpY, ClumpPeriod, 0);

                        float crumb = Specks(u, v, CrumbPeriod, CrumbChance, CrumbRadiusMin, CrumbRadiusMax, CrumbSalt, out float crumbDome);
                        float clod = Specks(u, v, ClodPeriod, ClodChance, ClodRadiusMin, ClodRadiusMax, ClodSalt, out float clodDome);
                        float pebble = Specks(u, v, PebblePeriod, PebbleChance, PebbleRadiusMin, PebbleRadiusMax, PebbleSalt, out float pebbleDome);

                        float shade = Mathf.Lerp(ClumpDark, ClumpLight, clump);
                        shade = Mathf.Lerp(shade, ClodShade, clod);
                        shade = Mathf.Lerp(shade, CrumbLight, crumb);
                        shade = Mathf.Lerp(shade, PebbleLight, pebble);
                        float value = Mathf.Clamp01(shade * DetailNeutral);
                        shades[y * Size + x] = new Color(value, value, value, 1f);

                        heights[y * Size + x] = clump * ClumpBump + crumbDome * CrumbBump + clodDome * ClodBump + pebbleDome * PebbleBump;
                    }
                }

                WritePng(SoilDetailPath, shades, Size, linear: true);
                WritePng(SoilNormalPath, NormalsFromHeights(heights), Size, linear: true);
                AssetDatabase.ImportAsset(SoilDetailPath, ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(SoilNormalPath, ImportAssetOptions.ForceSynchronousImport);
            }

            Configure(SoilCellPath, TextureImporterType.Default, sRgb: true);
            Configure(SoilDetailPath, TextureImporterType.Default, sRgb: false);
            Configure(SoilNormalPath, TextureImporterType.NormalMap, sRgb: false);
            return (AssetDatabase.LoadAssetAtPath<Texture2D>(SoilCellPath),
                AssetDatabase.LoadAssetAtPath<Texture2D>(SoilDetailPath),
                AssetDatabase.LoadAssetAtPath<Texture2D>(SoilNormalPath));
        }

        // 흙 칸 하나: 가장자리로 갈수록 어둡고(고랑), 가운데로 갈수록 조금 밝다(둔덕). 부드러운 그러데이션뿐이라 반복돼도 티가 나지 않는다.
        private static Color32[] DrawCell()
        {
            var pixels = new Color32[CellSize * CellSize];
            var center = new Vector2(0.5f, 0.5f);
            for (int y = 0; y < CellSize; y++)
            {
                for (int x = 0; x < CellSize; x++)
                {
                    float u = (x + 0.5f) / CellSize;
                    float v = (y + 0.5f) / CellSize;
                    float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                    float groove = Mathf.Lerp(GrooveShade, 1f, Mathf.SmoothStep(0f, 1f, edge / GrooveFade));
                    float fromCenter = Vector2.Distance(new Vector2(u, v), center);
                    float mound = Mathf.Lerp(MoundLight, 1f, Mathf.SmoothStep(0f, 1f, fromCenter / MoundFade));
                    Color color = Earth * (groove * mound);
                    color.a = 1f;
                    pixels[y * CellSize + x] = color;
                }
            }
            return pixels;
        }

        // 이웃 높이 차로 기울기를 재 탄젠트 공간 법선으로 바꾼다. 가장자리는 반대편에서 읽어 이음매가 없다.
        private static Color32[] NormalsFromHeights(float[] heights)
        {
            var normals = new Color32[heights.Length];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float left = heights[y * Size + (x + Size - 1) % Size];
                    float right = heights[y * Size + (x + 1) % Size];
                    float down = heights[((y + Size - 1) % Size) * Size + x];
                    float up = heights[((y + 1) % Size) * Size + x];
                    var normal = new Vector3((left - right) * NormalStrength, (down - up) * NormalStrength, 1f).normalized;
                    normals[y * Size + x] = new Color(normal.x * 0.5f + 0.5f, normal.y * 0.5f + 0.5f, normal.z * 0.5f + 0.5f, 1f);
                }
            }
            return normals;
        }

        /// <summary>
        /// 주기 격자 칸마다 몇 개씩 흩뿌린 타원 알갱이. 덮인 정도(0~1)를 돌려주고, dome에 볼록한 높이(0~1)를 준다.
        /// 크기·길쭉함·방향이 알갱이마다 달라 자연스럽다.
        /// </summary>
        private static float Specks(float u, float v, int period, float chance, float radiusMin, float radiusMax, int salt, out float dome)
        {
            float x = u * period;
            float y = v * period;
            int cellX = Mathf.FloorToInt(x);
            int cellY = Mathf.FloorToInt(y);
            float cover = 0f;
            dome = 0f;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int gx = cellX + dx;
                    int gy = cellY + dy;
                    int wx = Wrap(gx, period);
                    int wy = Wrap(gy, period);
                    for (int k = 0; k < SpecksPerCell; k++)
                    {
                        int s = (salt * SpecksPerCell + k) * SaltStride;
                        if (Hash(wx, wy, s) > chance) continue;

                        var center = new Vector2(gx + Hash(wx, wy, s + 1), gy + Hash(wx, wy, s + 2));
                        float radius = Mathf.Lerp(radiusMin, radiusMax, Hash(wx, wy, s + 3));
                        float aspect = Mathf.Lerp(MinAspect, 1f, Hash(wx, wy, s + 4));
                        float angle = Hash(wx, wy, s + 5) * Mathf.PI;
                        float cos = Mathf.Cos(angle);
                        float sin = Mathf.Sin(angle);
                        float px = x - center.x;
                        float py = y - center.y;
                        float along = (px * cos + py * sin) / radius;
                        float across = (-px * sin + py * cos) / (radius * aspect);
                        float distance = Mathf.Sqrt(along * along + across * across);
                        if (distance >= 1f) continue;

                        cover = Mathf.Max(cover, 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(SoftEdge, 1f, distance)));
                        dome = Mathf.Max(dome, Mathf.Sqrt(1f - distance * distance));
                    }
                }
            }
            return cover;
        }

        // 주기 값 노이즈를 옥타브마다 두 배 잘게 겹친다(0~1).
        private static float Fbm(float u, float v, int period, int salt)
        {
            float sum = 0f;
            float amplitude = 0.5f;
            float total = 0f;
            int octavePeriod = period;
            for (int octave = 0; octave < ClumpOctaves; octave++)
            {
                sum += PeriodicNoise(u * octavePeriod, v * octavePeriod, octavePeriod, salt + octave) * amplitude;
                total += amplitude;
                amplitude *= 0.5f;
                octavePeriod *= 2;
            }
            return sum / total;
        }

        // 주기 격자 값 노이즈(0~1). period마다 같은 값으로 돌아와 이음매 없이 반복된다.
        private static float PeriodicNoise(float x, float y, int period, int salt)
        {
            int ix = Mathf.FloorToInt(x);
            int iy = Mathf.FloorToInt(y);
            float fx = x - ix;
            float fy = y - iy;
            float sx = fx * fx * (3f - 2f * fx);
            float sy = fy * fy * (3f - 2f * fy);
            float a = Hash(Wrap(ix, period), Wrap(iy, period), salt);
            float b = Hash(Wrap(ix + 1, period), Wrap(iy, period), salt);
            float c = Hash(Wrap(ix, period), Wrap(iy + 1, period), salt);
            float d = Hash(Wrap(ix + 1, period), Wrap(iy + 1, period), salt);
            return Mathf.Lerp(Mathf.Lerp(a, b, sx), Mathf.Lerp(c, d, sx), sy);
        }

        private static int Wrap(int value, int period) => ((value % period) + period) % period;

        private static float Hash(int x, int y, int salt)
        {
            float n = Mathf.Sin(x * HashX + y * HashY + salt * HashSalt) * HashScale;
            return n - Mathf.Floor(n);
        }

        private static void WritePng(string path, Color32[] pixels, int size, bool linear)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, linear);
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
            importer.SaveAndReimport();
        }
    }
}
