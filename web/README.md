# 랜덤 게임 v2.0 (웹 버전)

데스크톱 v1.2(`랜덤게임_v1.2.exe`)의 4개 탭을 **Rust(Leptos) → WebAssembly**로 옮긴 웹 페이지입니다.

- 🎡 연속 룰렛: 연속 / 선승제(2~5선승) / 단판, 자동 돌리기, 속도 4단계, 메모장
- ┣┫ 사다리 게임: 가림막, 가로선 직접 추가(클릭)·삭제(우클릭), 순서대로 자동 출발
- 🎫 제비뽑기: 당첨·꽝 / 순서 뽑기 / 직접 편집, 뽑는 사람 명단
- 👥 팀 나누기: 2~12팀, 시드 분산(초과 경고), 1~5티어 균등 분배, 팀장 뽑기, 기록 복원

서버가 필요 없는 **정적 사이트**이고, 모든 데이터는 브라우저 메모리에만 있습니다(새로고침하면 초기화, 파일·저장소에 저장 안 함).

## 빌드 / 로컬 실행 (Windows)

```
powershell -ExecutionPolicy Bypass -File .\web\build.ps1          # web\dist 생성
powershell -ExecutionPolicy Bypass -File .\web\build.ps1 -Serve   # http://127.0.0.1:8080
```

필요한 도구 (이미 이 PC에 설치됨):
- Rust stable (`x86_64-pc-windows-gnu`) + `wasm32-unknown-unknown` 타깃 — `%USERPROFILE%\.cargo`
- trunk 0.22 — `%USERPROFILE%\.cargo\bin\trunk.exe`
- mingw binutils (WinLibs) — `%USERPROFILE%\.mingw-winlibs` (빌드 시 proc-macro 링크용)

> 프로젝트 경로에 한글·전각 공백이 있어 mingw 도구가 경로를 못 읽기 때문에, `build.ps1`은 중간 빌드 파일을
> `%USERPROFILE%\.cargo\target-random-game-web`에 둡니다.

## 웹 호스팅

`web\dist` 폴더 안의 파일(index.html, .js, .wasm, .css, .ico)을 그대로 올리면 됩니다.

- **Netlify / Cloudflare Pages**: 사이트 만들기 → `web\dist` 폴더를 끌어다 놓기. (도메인 루트에 올라가므로 그대로 동작)
- **GitHub Pages**: 저장소에 이 프로젝트를 올리고 Settings → Pages → Source = **GitHub Actions**.
  `.github/workflows/deploy-web.yml`이 main에 push될 때마다 리눅스에서 빌드해 배포합니다.
  (`https://<계정>.github.io/<저장소>/` 하위 경로에 맞게 `--public-url`을 자동 지정)
- 하위 폴더에 직접 올릴 때는 `trunk build --release --public-url /폴더이름/` 으로 빌드하세요.

웹서버가 `.wasm` 파일을 `application/wasm` 형식으로 보내야 합니다(위 서비스들은 기본 지원).

## 소스 구조

| 파일 | 내용 |
|---|---|
| `src/main.rs` | 앱 틀, 상단 탭, 여러 줄 입력 대화상자 |
| `src/util.rs` | 공정 난수(crypto), 효과음(WebAudio 합성), 클립보드, 캔버스 도우미 |
| `src/roulette.rs` | 탭 0 연속 룰렛 |
| `src/ladder.rs` | 탭 1 사다리 게임 |
| `src/lots.rs` | 탭 2 제비뽑기 |
| `src/team.rs` | 탭 3 팀 나누기 |
| `style.css` | v1.2 디자인(그래파이트 + 인디고) |
