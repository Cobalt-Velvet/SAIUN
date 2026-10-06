# SkyLab

SAIUN 하늘·바다 셰이더의 원본과 시안 실험실입니다. Unity의 `Assets/_SAIUN/Art/Shaders/Sky.shader` 는 이 폴더에서 만들어 냅니다.

| 파일 | 하는 일 |
| --- | --- |
| `sky_common.glsl` | 하늘 본문: 대기 산란, 구름 전 종류, 웅대적운 탑, 채운, 겹쳐 그리기 |
| `sea_common.glsl` | 보이는 장: 두 장 섞기, 바다·윤슬·모래밭, 빛내림 |
| `light_common.glsl`, `view_common.glsl` | 해·달 빛, 보는 방향(카메라) |
| `lab_*.glsl`, `sky_template.html` | WebGL 시안용 머리·꼬리와 페이지 틀 |
| `build.py` | 시안 페이지 `sky.html` 만들기 |
| `server.py`, `runner.html`, `variants.txt` | 시안을 줄마다 그려 `out/` 에 PNG로 저장(localhost 전용) |
| `sheet.py`, `waitv.sh` | 저장된 시안을 한 장으로 모으기 |
| `to_hlsl.py` | GLSL을 HLSL로 옮겨 `Sky.shader` 만들기 |
| `fxc_check.py` | `Sky.shader` 패스를 D3DCompile로 직접 컴파일해 경고 보기 |
| `comb3.py` | 캡처에서 4×2 칸이 어긋난 빗살이 있는지 재기 |

```sh
python build.py          # sky.html
python server.py         # http://127.0.0.1:8765/runner.html
python to_hlsl.py        # ../../Assets/_SAIUN/Art/Shaders/Sky.shader
```

`variants.txt` 한 줄은 `이름 쿼리` 입니다. 쿼리 예: `progress`(하루 0~1), `low`·`mid`·`hi`·`ex`·`sp`(구름 덮임),
`seed`(탑 모양), `growth`(탑 높이), `form`·`lp`(채운 모양·자리), `view`(올려다보는 각·초점·돈 각).
