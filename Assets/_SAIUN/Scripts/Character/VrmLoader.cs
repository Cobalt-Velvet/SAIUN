using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using _SAIUN.Scripts.Data;
using UniGLTF;
using UniVRM10;
using UnityEngine;
using UnityEngine.Rendering;

namespace _SAIUN.Scripts.Character
{
    /// <summary>VRM을 불러오지 못한 이유 (사양서 v1.1 8-2).</summary>
    public enum VrmLoadError
    {
        None,
        FileNotFound,
        TooLarge,
        Unreadable,
        NoHumanoid,
    }

    /// <summary>
    /// 캐릭터 VRM을 런타임에 불러온다 (P3-01, 사양서 v1.1 8-1·8-2).
    /// 시작하면 저장된 경로(character.vrmPath)를, 비어 있거나 실패하면 번들 기본 캐릭터를 불러온다.
    /// VRM 0.x는 1.0으로 옮겨 읽고, 머티리얼은 URP용 MToon10으로 만든다(8-8 분홍 fallback 해결).
    /// 이 프로젝트는 URP 전용이라 머티리얼 생성기를 URP로 고정한다.
    /// 불러오기에 성공해야 경로를 저장하므로, 도중에 앱을 끄면 이전 캐릭터 경로가 그대로 남는다.
    /// 실패하면 지금 캐릭터를 유지하고 이유를 알린다.
    /// </summary>
    public class VrmLoader : MonoBehaviour
    {
        /// <summary>사양서 8장: VRM 파일 200MB 이하.</summary>
        public const long MaxFileBytes = 200L * 1024 * 1024;

        /// <summary>StreamingAssets 아래 번들 기본 캐릭터 경로.</summary>
        public const string BundledRelativePath = "Characters/default.vrm";

        [Tooltip("불러온 모델을 붙일 자리. 위치·방향은 이 트랜스폼이 정한다.")]
        [SerializeField] private Transform characterRoot;

        [Header("표시 (실측 대기)")]
        [Tooltip("모델 배율. VRM은 미터 단위라 창 100px = 1m에서는 작게 보인다.")]
        [SerializeField, Min(0.01f)] private float modelScale = 2.4f;

        [Tooltip("캐릭터가 씬에 그림자를 드리울지. 창 베젤에 앉아 있어 화단 바닥과 떨어져 있으므로 기본은 끈다.")]
        [SerializeField] private bool castShadows;

        /// <summary>지금 캐릭터 모델의 루트. 없으면 null.</summary>
        public GameObject CurrentModel { get; private set; }

        /// <summary>지금 캐릭터 파일 경로. 없으면 null.</summary>
        public string CurrentPath { get; private set; }

        /// <summary>지금 캐릭터가 번들 기본 캐릭터인지.</summary>
        public bool IsBundled => CurrentPath != null && CurrentPath == BundledPath;

        public bool IsLoading { get; private set; }

        /// <summary>휴머노이드 뼈대가 온전해 포즈를 바꿀 수 있는지. 아니면 T 포즈로 둔다(8-2).</summary>
        public bool PoseSupported { get; private set; }

        /// <summary>번들 기본 캐릭터 전체 경로.</summary>
        public static string BundledPath => Path.Combine(Application.streamingAssetsPath, BundledRelativePath);

        /// <summary>새 캐릭터를 붙였을 때.</summary>
        public event Action<GameObject> OnCharacterLoaded;

        /// <summary>불러오지 못했거나 일부 기능을 쓸 수 없을 때. 인자는 이유와 자세한 내용.</summary>
        public event Action<VrmLoadError, string> OnLoadFailed;

        private CancellationTokenSource _cancel;

        // ---- 수명 주기 ----

        private void Awake()
        {
            if (characterRoot == null) characterRoot = transform;
            _cancel = new CancellationTokenSource();
        }

        private async void Start()
        {
            await LoadInitialAsync();
        }

        private void OnDestroy()
        {
            // 불러오는 중에 앱이 꺼지면 멈춘다. 경로는 성공했을 때만 저장하므로 이전 값이 남는다.
            _cancel?.Cancel();
            _cancel?.Dispose();
            _cancel = null;
            DestroyModel();
        }

        // ---- 공개 API ----

        /// <summary>사용자가 고른 파일을 불러온다. 성공하면 경로를 저장한다.</summary>
        public Task<bool> ImportAsync(string path) => LoadAsync(path, persist: true);

        /// <summary>번들 기본 캐릭터로 되돌리고 저장된 경로를 지운다.</summary>
        public async Task<bool> ResetToBundledAsync()
        {
            bool loaded = await LoadAsync(BundledPath, persist: false);
            if (loaded) SettingsStore.VrmPath = SettingsStore.DefaultVrmPath;
            return loaded;
        }

        /// <summary>
        /// 파일을 불러와 지금 캐릭터와 바꾼다. 실패하면 지금 캐릭터를 그대로 둔다.
        /// persist면 성공했을 때 경로를 저장한다.
        /// </summary>
        public async Task<bool> LoadAsync(string path, bool persist)
        {
            VrmLoadError precheck = Validate(path, out string detail);
            if (precheck != VrmLoadError.None)
            {
                Fail(precheck, detail);
                return false;
            }

            if (IsLoading) return false;
            IsLoading = true;
            Vrm10Instance instance;
            try
            {
                instance = await Vrm10.LoadPathAsync(
                    path,
                    canLoadVrm0X: true,
                    // 뼈를 직접 다루는 포즈(P3-04)와 휴머노이드 근육 API가 그대로 먹도록 제어 리그를 만들지 않는다.
                    controlRigGenerationOption: ControlRigGenerationOption.None,
                    showMeshes: false,
                    // UniVRM의 자동 판별은 RenderPipelineManager.currentPipeline을 보는데, 첫 프레임을 그리기 전(Start)에는
                    // 비어 있어 Built-in용 MToon을 고른다. 그러면 URP에서 분홍으로 깨지므로 URP용을 직접 지정한다.
                    materialGenerator: new UrpVrm10MaterialDescriptorGenerator(),
                    ct: _cancel != null ? _cancel.Token : CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception e)
            {
                Fail(VrmLoadError.Unreadable, e.Message);
                return false;
            }
            finally
            {
                IsLoading = false;
            }

            if (instance == null)
            {
                Fail(VrmLoadError.Unreadable, "UniVRM이 모델을 돌려주지 않았습니다.");
                return false;
            }

            Attach(instance.gameObject);
            CurrentPath = path;
            if (persist) SettingsStore.VrmPath = path;
            OnCharacterLoaded?.Invoke(CurrentModel);

            if (!PoseSupported) Fail(VrmLoadError.NoHumanoid, "휴머노이드 뼈대가 온전하지 않습니다.");
            return true;
        }

        /// <summary>파일을 열기 전에 확인할 수 있는 실패 이유. 문제없으면 None.</summary>
        public static VrmLoadError Validate(string path, out string detail)
        {
            detail = path;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return VrmLoadError.FileNotFound;

            long size = new FileInfo(path).Length;
            if (size > MaxFileBytes)
            {
                detail = $"{size / (1024 * 1024)}MB";
                return VrmLoadError.TooLarge;
            }
            return VrmLoadError.None;
        }

        // ---- 내부 ----

        private async Task LoadInitialAsync()
        {
            string saved = SettingsStore.VrmPath;
            if (!string.IsNullOrEmpty(saved) && await LoadAsync(saved, persist: false)) return;

            // 저장된 캐릭터를 못 읽으면 기본 캐릭터로 시작한다. 기본 캐릭터가 없으면 캐릭터 없이 돈다.
            if (File.Exists(BundledPath)) await LoadAsync(BundledPath, persist: false);
        }

        private void Attach(GameObject model)
        {
            DestroyModel();

            CurrentModel = model;
            model.transform.SetParent(characterRoot, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one * modelScale;

            var animator = model.GetComponent<Animator>();
            PoseSupported = animator != null && animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman;

            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            }

            if (model.TryGetComponent(out RuntimeGltfInstance gltf))
            {
                gltf.ShowMeshes();
                // 창 크기가 작아 경계 상자가 어긋나면 모델이 깜빡 사라진다.
                gltf.EnableUpdateWhenOffscreen();
            }
        }

        private void DestroyModel()
        {
            if (CurrentModel == null) return;
            // 텍스처·메시 같은 런타임 리소스까지 함께 치운다.
            if (CurrentModel.TryGetComponent(out RuntimeGltfInstance gltf)) gltf.Dispose();
            else Destroy(CurrentModel);
            CurrentModel = null;
        }

        private void Fail(VrmLoadError error, string detail)
        {
            Debug.LogWarning($"VrmLoader: {error} ({detail})");
            OnLoadFailed?.Invoke(error, detail);
        }
    }
}
