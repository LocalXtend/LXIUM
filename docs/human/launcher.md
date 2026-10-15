# LXIUM 런처

게임 최적화 모드를 설치하고 제거하는 프로그램입니다. 설치 없이 압축만 풀어서 씁니다.

## 쓰는 법

1. `LXIUM-<버전>-portable.zip`을 원하는 곳에 풀고 `LXIUM.exe`를 실행합니다.
2. 라이브러리에 모드가 있는 게임이 카드로 나옵니다. 게임은 Steam 라이브러리 폴더를 모두 뒤져서 찾습니다.
3. 게임 카드를 누르면 자세한 화면이 열립니다.
   - 게임 정보 (IGDB)
   - 게임 폴더, 게임 실행 버튼
   - 게임에서 찾은 모드 로더 (BepInEx, MelonLoader 등). 여러 개면 모드를 설치할 로더를 고릅니다.
   - 최적화 모드 설치·업데이트·제거 버튼
4. 제거하면 모드가 덮어쓴 원래 파일까지 되돌립니다. 모드 설정 파일은 남습니다.

모드 로더는 아직 런처가 설치해 주지 않습니다. 로더가 없으면 어떤 로더가 필요한지 알려 주니, 그 로더를 먼저 설치하세요.

## 게임 정보 (IGDB) 켜기

IGDB는 Twitch 개발자 키가 있어야 합니다.

1. https://dev.twitch.tv/console 에서 애플리케이션을 만들고 Client ID와 Client Secret을 받습니다.
2. `LXIUM.exe` 옆에 `settings.json`을 만듭니다.

```json
{
  "igdbClientId": "받은 Client ID",
  "igdbClientSecret": "받은 Client Secret"
}
```

키가 없어도 모드 설치·제거는 됩니다.

## 빌드 (개발자)

저장소 루트에서 `./build.ps1`을 실행하면 `dist/LXIUM-<버전>-portable.zip`이 만들어집니다.
`optimizeMods/`의 각 모드 프로젝트에서 `ver.txt` 버전과 같은 zip을 `dist/`에 먼저 만들어 두어야 함께 들어갑니다.
