using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace _SAIUN.Editor
{
    /// <summary>
    /// 효과음 5종의 임시 음원을 합성해 WAV로 저장한다 (P5-01, 사양서 v1.1 13-1).
    /// 외부 음원을 쓰지 않아 라이선스 걱정이 없다. 실제 음원이 들어오면 같은 이름의 파일을 덮어쓰거나
    /// SoundView의 클립만 바꾼다. 파일이 이미 있으면 건드리지 않는다.
    /// </summary>
    public static class SoundPlaceholderBuilder
    {
        public const string Folder = "Assets/_SAIUN/Audio/SFX";
        public const string Complete = "sfx_complete";
        public const string Transition = "sfx_transition";
        public const string Harvest = "sfx_harvest";
        public const string Warning = "sfx_warning";
        public const string Death = "sfx_death";

        private const int SampleRate = 44100;
        private const float PeakLevel = 0.8f;       // 최대 크기. 1에 붙이면 재생 장치에 따라 깨질 수 있다.
        private const float TargetRms = 0.16f;      // 체감 음량 목표. 피크만 맞추면 지속음인 경고음이 세 배 가까이 크게 들린다.
        private const float AttackSeconds = 0.006f; // 소리 머리의 딸깍임을 없애는 짧은 상승

        // 음높이(Hz). 평균율 A4 = 440 기준.
        private const float A3 = 220f;
        private const float C4 = 261.63f;
        private const float E4 = 329.63f;
        private const float C5 = 523.25f;
        private const float E5 = 659.25f;
        private const float F5 = 698.46f;
        private const float G5 = 783.99f;
        private const float A5 = 880f;
        private const float B5 = 987.77f;
        private const float C6 = 1046.5f;
        private const float E6 = 1318.51f;

        [MenuItem("SAIUN/Build Sound Placeholders (missing only)")]
        public static void BuildMissingMenu()
        {
            BuildMissing();
        }

        /// <summary>없는 음원을 만들고, 이름별 클립을 돌려준다.</summary>
        public static Dictionary<string, AudioClip> BuildMissing()
        {
            EnsureFolder(Folder);

            var recipes = new Dictionary<string, Func<float[]>>
            {
                // 세트 전환(집중 → 휴식): 내려앉는 두 음. 긴장이 풀리는 느낌.
                [Transition] = () => Mix(1.1f,
                    Bell(G5, 0f, 0.9f, 0.5f),
                    Bell(E5, 0.18f, 0.9f, 0.5f)),

                // 포모도로 완료: 도-미-솔-도 상행 아르페지오, 마지막 음을 길게.
                [Complete] = () => Mix(2.2f,
                    Bell(C5, 0f, 0.8f, 0.45f),
                    Bell(E5, 0.13f, 0.8f, 0.45f),
                    Bell(G5, 0.26f, 0.9f, 0.45f),
                    Bell(C6, 0.39f, 1.7f, 0.55f)),

                // 수확: 빠르게 튀어 오르는 반짝임과 그 밑의 화음.
                [Harvest] = () => Mix(1.4f,
                    Bell(E5, 0f, 0.35f, 0.35f),
                    Bell(G5, 0.06f, 0.35f, 0.35f),
                    Bell(B5, 0.12f, 0.4f, 0.35f),
                    Bell(E6, 0.18f, 1.1f, 0.4f),
                    Bell(C5, 0.18f, 1.1f, 0.25f)),

                // 방해 앱 경고: 두 음을 짧게 번갈아. 거슬리지 않게 사인파 위주로 둔다.
                [Warning] = () => Mix(0.75f,
                    Beep(A5, 0f, 0.13f),
                    Beep(F5, 0.17f, 0.13f),
                    Beep(A5, 0.34f, 0.13f),
                    Beep(F5, 0.51f, 0.13f)),

                // 작물 사망: 낮게 내려가는 단조 세 음.
                [Death] = () => Mix(1.8f,
                    Soft(E4, 0f, 0.7f),
                    Soft(C4, 0.32f, 0.7f),
                    Soft(A3, 0.64f, 1.1f)),
            };

            var clips = new Dictionary<string, AudioClip>();
            foreach (KeyValuePair<string, Func<float[]>> recipe in recipes)
            {
                string assetPath = $"{Folder}/{recipe.Key}.wav";
                if (!File.Exists(assetPath))
                {
                    WriteWav(assetPath, Normalize(recipe.Value()));
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                }
                clips[recipe.Key] = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
            }
            return clips;
        }

        // ---- 음 ----

        private delegate float Voice(float time);

        /// <summary>종소리: 기음 위에 배음을 조금 얹고 지수적으로 사라진다.</summary>
        private static Voice Bell(float frequency, float start, float length, float amplitude)
        {
            return t => Envelope(t - start, length) * amplitude * Harmonics(frequency, t - start, 0.35f, 0.12f);
        }

        /// <summary>짧은 신호음. 배음을 거의 넣지 않는다.</summary>
        private static Voice Beep(float frequency, float start, float length)
        {
            const float amplitude = 0.4f;
            return t =>
            {
                float local = t - start;
                if (local < 0f || local > length) return 0f;
                // 끝을 부드럽게 닫아 딸깍임을 없앤다.
                float gate = Mathf.Clamp01(local / AttackSeconds) * Mathf.Clamp01((length - local) / AttackSeconds);
                return gate * amplitude * Harmonics(frequency, local, 0.15f, 0f);
            };
        }

        /// <summary>둥근 저음. 배음을 빼고 천천히 사라진다.</summary>
        private static Voice Soft(float frequency, float start, float length)
        {
            const float amplitude = 0.55f;
            return t => Envelope(t - start, length) * amplitude * Harmonics(frequency, t - start, 0.2f, 0f);
        }

        private static float Harmonics(float frequency, float time, float second, float third)
        {
            float phase = 2f * Mathf.PI * frequency * time;
            return Mathf.Sin(phase) + second * Mathf.Sin(phase * 2f) + third * Mathf.Sin(phase * 3f);
        }

        /// <summary>짧게 올라가고 length 동안 지수적으로 사라진다. length 뒤에는 0.</summary>
        private static float Envelope(float local, float length)
        {
            if (local < 0f || local > length) return 0f;
            float attack = Mathf.Clamp01(local / AttackSeconds);
            // length 끝에서 약 -40dB가 되게 한다.
            const float decayToQuiet = 4.6f;
            return attack * Mathf.Exp(-decayToQuiet * local / length);
        }

        private static float[] Mix(float seconds, params Voice[] voices)
        {
            var samples = new float[Mathf.CeilToInt(seconds * SampleRate)];
            for (int i = 0; i < samples.Length; i++)
            {
                float time = i / (float)SampleRate;
                float sum = 0f;
                foreach (Voice voice in voices) sum += voice(time);
                samples[i] = sum;
            }
            return samples;
        }

        // 다섯 소리의 체감 음량(RMS)을 맞추되 피크가 PeakLevel을 넘지 않게 한다.
        private static float[] Normalize(float[] samples)
        {
            float peak = 0f;
            double sumSquares = 0d;
            foreach (float sample in samples)
            {
                peak = Mathf.Max(peak, Mathf.Abs(sample));
                sumSquares += sample * sample;
            }
            if (peak <= 0f) return samples;

            float rms = Mathf.Sqrt((float)(sumSquares / samples.Length));
            float gain = Mathf.Min(PeakLevel / peak, TargetRms / rms);
            for (int i = 0; i < samples.Length; i++) samples[i] *= gain;
            return samples;
        }

        // ---- WAV ----

        /// <summary>16비트 PCM 모노 WAV로 쓴다.</summary>
        private static void WriteWav(string path, float[] samples)
        {
            const short channels = 1;
            const short bitsPerSample = 16;
            const int bytesPerSample = bitsPerSample / 8;
            int dataSize = samples.Length * bytesPerSample;

            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var writer = new BinaryWriter(stream);
            writer.Write(new[] { 'R', 'I', 'F', 'F' });
            writer.Write(36 + dataSize);
            writer.Write(new[] { 'W', 'A', 'V', 'E' });
            writer.Write(new[] { 'f', 'm', 't', ' ' });
            writer.Write(16);                                         // fmt 청크 크기
            writer.Write((short)1);                                   // PCM
            writer.Write(channels);
            writer.Write(SampleRate);
            writer.Write(SampleRate * channels * bytesPerSample);     // 초당 바이트
            writer.Write((short)(channels * bytesPerSample));         // 블록 정렬
            writer.Write(bitsPerSample);
            writer.Write(new[] { 'd', 'a', 't', 'a' });
            writer.Write(dataSize);
            foreach (float sample in samples)
            {
                writer.Write((short)Mathf.RoundToInt(Mathf.Clamp(sample, -1f, 1f) * short.MaxValue));
            }
        }

        private static void EnsureFolder(string assetFolder)
        {
            if (string.IsNullOrEmpty(assetFolder) || AssetDatabase.IsValidFolder(assetFolder)) return;

            string parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(assetFolder));
        }
    }
}
