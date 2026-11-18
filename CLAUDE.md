# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

> 2026-11-17 시작. 단계가 진행될 때마다 `(TBD)` 부분을 실제 내용으로 채운다. 작업 순서와 배경은 `Docs/Dev/01_프로젝트환경구축.md`, 전투 설계는 `Docs/Dev/02_전투뼈대설계.md`에 있다.

## Project

- 가제 미정 (세계관 문서명은 "PROJECT : VITA"). 스토리 게임에 **포인트앤클릭 탐색**과 **포켓몬식 스탯 기반 턴제 전투**(인물이 싸우고, 인조 생물은 적)를 섞은 2D 게임이다.
- 목표
  - 2027년 3월 행사용 데모 (03-22 데브캠프, 03-31 스마일게이트 공모전, 04-06 인디크래프트, 04-10 BIC)
  - 2027년 5월 전후 스토브 / itch.io / 스팀 출시
- Unity **6000.3.11f1** (Unity 6.3 LTS), URP 2D 템플릿. 새 PC에도 같은 버전을 설치한다.
- Git 루트 = Unity 프로젝트 루트. GitHub `cutesiha/PROJECTVITA`.
- 팀
  - **사용자** (팀장): 프로그래밍, 디렉팅, 기획 정리, 빌드, QA
  - **비디**: 시나리오, 캐릭터, 기획 전반 (전투 규칙 포함)
  - **두부**: 일러스트, 배경, UI 아트
  - 비디와 두부는 Git과 Unity를 모른다. 대사는 VS Code로 쓰고, GitHub Desktop으로 `dialogue` 브랜치에 올린다.
- 레퍼런스는 림버스 컴퍼니, 네가 죽어, 단간론파다. UI와 연출의 감각만 참고하고, 에셋, 레이아웃, 로고는 베끼지 않는다.
- **이전 팀 프로젝트(BlackJokerTrials)와 그 계열(Devdup, DOS, HelpDoNyang)의 코드, 프리팹, 아트, 유료 에셋은 쓰지 않는다.** 이 레포의 모든 시스템은 새로 작성한 것이다.
- 기획(스토리, 캐릭터, 전투 규칙)은 비디의 영역이다. Claude는 대신 확정하지 않고, 필요하면 질문 목록이나 양식을 만들어 사용자에게 준다.

## Commands

| Task | How |
|---|---|
| 기본 세팅 (Player Settings, TMP, 폰트, 빌드 씬) | `Tools > Project Vita > 1단계 기본 세팅 적용` (여러 번 실행해도 안전) |
| 커밋 전 Dynamic 폰트 비우기 | `Tools > Project Vita > Dynamic 폰트 비우기 (커밋 전)` |
| 씬 생성 (Yarn Project, 리소스 목록, CSV 임포트, 씬 4개, 빌드 씬) | `Tools > Project Vita > 씬 생성 (Title, Story, Exploration, Battle)`. 씬을 처음부터 다시 만든다. 씬은 손으로 고치지 말고 `Scripts/Editor/DialogueSetup.cs`를 고친다 |
| 리소스 목록 갱신 | 자동 (Art, Audio 폴더나 `Characters.csv`가 바뀌면 `Resources/AssetCatalog.asset`이 저절로 갱신됨). 수동: `Tools > Project Vita > 리소스 목록 갱신` |
| 임시 그림과 소리 만들기 | `Tools > Project Vita > 임시 그림과 소리 만들기`. 목록은 `Scripts/Editor/PlaceholderArt.cs`. **이미 있는 파일은 건너뛴다** (최종 아트를 덮어쓰지 않음) |
| 임시 UI 그림 만들기 | `Tools > Project Vita > 임시 UI 그림 만들기` (씬 생성이 먼저 부른다). `Art/UI/*.png`(깎인 패널 9-slice, 아이콘, 육각형, 스캔라인 등, 흰색)와 UI 폰트 에셋. 이미 있는 파일은 건너뛴다. 두부님 UI 아트는 같은 파일 이름으로 덮어쓴다 |
| 에피소드 표 검사 | `Tools > Project Vita > 에피소드 표 검사` (start_node 노드 존재, next_episode, 씬 등록 확인) |
| 대사 플레이 | `Title` 씬에서 Play → `처음부터` (표의 첫 에피소드) 또는 오른쪽 `에피소드 선택 (개발용)`. `Story` 씬을 바로 Play하면 `StoryDirector.fallbackEpisodeId` (비면 첫 에피소드) |
| 에피소드 목록 | `Assets/_Project/Data/Episodes.csv`: `episode_id, display_name, unity_scene, start_node, next_episode`. 행을 추가하면 타이틀의 에피소드 버튼도 늘어난다 |
| CSV 임포트 (탐색, 전투) | 자동 (`Data/Exploration_*.csv`, `Battle_*.csv`가 바뀌면). 수동: `Tools > Project Vita > CSV 임포트 (탐색, 전투)`. 없는 Yarn 노드, 중복 id, 없는 스킬/상태이상/유닛, 효과 문법, 조건식 오류를 행 번호와 함께 경고 |
| 전투 시뮬레이터 | `Tools > Project Vita > 전투 시뮬레이터` (조우, 반복 횟수, 아군 AI 랜덤/탐욕 → 승률, 평균 라운드, 유닛별 피해, 스킬 빈도, CSV 저장). 코드에서는 `BattleSimulatorWindow.Simulate(...)` |
| 탐색, 전투 예시 | 타이틀의 개발용 에피소드 선택에서 `[예시] 탐색 확인`(ExploreTest), `[예시] 전투 확인`(BattleTest). 대사는 `Dialogue/_Example/ExploreExample.yarn`, `BattleExample.yarn` |
| 대사 테스트 러너 | `Tools > Project Vita > 대사 테스트 러너`. 노드를 누르면 그 노드부터 플레이한다 (변수는 비우고 창의 "시작 변수"를 넣음, 끝나면 원래 씬으로 돌아옴). "읽은 대사 기록 지우기" 버튼도 있다 |
| 핫리로드 | 플레이 중에 `.yarn`을 저장하면 1~2초 뒤 지금 노드(탐색이면 장소나 지도)를 처음부터 다시 연다. 게임 안에서 F5도 같다 (개발 빌드) |
| 세이브 파일 | `%USERPROFILE%/AppData/LocalLow/TeamVita/PROJECTVITA/`: `autosave.json`, `save_1~7.json`과 썸네일 `.png`, `read_lines.json`(읽은 대사). 지우면 처음 상태가 된다 |
| 테스트 | 없음 |
| 에디터 없이 컴파일 확인 | Unity가 만든 `Assembly-CSharp.csproj`는 `dotnet build`로 빌드된다. 새 `.cs`는 Unity가 csproj를 다시 만들기 전까지 빠져 있으니, 복사본 csproj에 `<Compile Include>`를 추가해서 확인한다 |

**Windows에서 Python을 실행할 때**: `python3`이나 `python`이 Microsoft Store 스텁으로 연결되어 있으면, `Python`만 출력하고 exit 49로 끝난다. 실제 설치 여부를 먼저 확인한다. 없으면 `dotnet`으로 대체하거나 사용자에게 설치를 묻는다.

## Branches

- `main`: 사용자가 관리한다. 코드, 씬, 반영이 끝난 대사가 있다.
- `ui`: UI 작업 브랜치 (2026-11-18, `main`의 4-7 커밋 위에서 시작). 사용자가 확인하고 `main`에 머지한다.
- `dialogue`: 팀원이 `Assets/_Project/Dialogue/**/*.yarn`만 커밋한다. 사용자가 `main`으로 머지하고, 작가의 `// 연출 메모`를 명령으로 바꾼다.
- `.meta`는 사용자만 커밋한다. 작가가 만든 새 `.yarn`의 `.meta`는 사용자가 Unity에서 열 때 생성된다.
- **Claude는 커밋하지 않는다.** 사용자가 GitHub Desktop으로 직접 커밋한다. 푸시, 머지, 브랜치 생성은 사용자가 요청할 때만 한다.
- Unity가 열린 작업 폴더에서 브랜치를 체크아웃하지 않는다 (에셋이 다시 임포트된다). 브랜치를 맞출 때는 `git push origin main:dialogue`처럼 포인터만 옮긴다.

## Dialogue (Yarn Spinner)

- Yarn Spinner for Unity **v3.3.2** (무료 Git 패키지, `manifest.json`에 `#v3.3.2` 태그로 고정). 올릴 때는 태그만 바꾼다. 3.3 문서는 `yarnspinner.dev/docs/unity/`에 있다 (`docs.yarnspinner.dev`는 3.2 이하).
- `.yarn` 파일은 `Assets/_Project/Dialogue/` 아래에 있다 (Yarn Spinner가 임포트하려면 Assets 안이어야 한다). `Dialogue/Dialogue.yarnproject` 하나가 `**/*.yarn`으로 이 폴더 전체를 포함하므로, 새 파일은 자동으로 들어간다.
- 예시와 문법 확인은 `Dialogue/_Example/Example.yarn` (에피소드 `Example`). 프롤로그와 챕터 1-1은 임시 대사다.
- 작가 문법
  - `화자: 대사`
  - 화자가 없으면 나레이션이고, 이름표를 숨긴다
  - 선택지는 `->`
  - 연출 메모는 `//` (주석이라 게임에 나오지 않는다)
- 노드 이름은 `{EpisodeId}_{장면}`이고 영어, 숫자, `_`만 쓴다 (예: `Prologue_Start`, `Ch1_01_Start`).
- 명령은 `Scripts/Runtime/Story/StoryCommands.cs`의 `[YarnCommand]` 정적 메서드다. Yarn이 컴파일할 때 자동 등록하고, 명령 정의를 `ProjectSettings/Packages/dev.yarnspinner/*.ysls.json`에 생성한다 (커밋한다. VS Code 확장도 이것으로 명령을 검사한다). `[ ]`는 생략 가능.

  | 명령 | 기다림 | 비고 |
  |---|---|---|
  | `<<bg 이름 [cut\|fade\|wipe] [시간]>>` | O | `none`이면 배경 지움. 기본 fade 0.6 |
  | `<<cg 이름 [cut\|fade] [시간]>>`, `<<cg off>>` | O | 스탠딩 위에 뜬다 |
  | `<<show 캐릭터 [표정] [위치] [시간]>>` | X | 위치 `left center right farleft farright`. 표정 없이 위치만 써도 됨. 이미 있으면 표정과 위치만 바꿈 |
  | `<<hide 캐릭터 [시간]>>`, `<<hide all>>` | X | |
  | `<<move 캐릭터 위치 [시간]>>` | X | |
  | `<<face 캐릭터 표정>>` | X | 줄 끝 태그 `#face:표정`도 됨 (그 줄의 화자에게 적용) |
  | `<<fade out [시간] [black\|white\|#색]>>`, `<<fade in [시간]>>` | O | 대사창까지 덮는다. fade in 뒤에 대사를 쓴다 |
  | `<<shake [세기] [시간]>>` | X | 세기 1 = 약 30px. 무대만 흔든다 |
  | `<<flash [색] [시간]>>`, `<<vignette on\|off\|0~1>>` | X | |
  | `<<bgm 이름 [페이드]>>`, `<<bgm stop [페이드]>>`, `<<sfx 이름 [볼륨]>>` | X | BGM은 에피소드가 바뀌어도 이어진다 |
  | `<<episode episode_id>>` | - | 즉시 이동 |
  | `<<wait 초>>`, `<<stop>>` | O | Yarn 내장 |
  | `<<explore 장소id>>` | - | 탐색 씬의 그 장소로 (Exploration_Location.csv) |
  | `<<battle 조우id>>` | - | 전투로. 끝나면 시작한 씬(이야기 또는 탐색 장소)으로 돌아와 win_node/lose_node 실행. BGM도 전투 전으로 |
  | `<<title>>` | - | 타이틀로 |

  - 캐릭터는 id(`Protagonist`)나 Yarn 화자 이름(`주인공`) 둘 다 된다. 표정, 배경, 소리 이름은 파일 이름이다.
  - 등록되지 않은 명령은 Yarn이 일반 로그만 남기므로, `StoryPresenter.RunCommand`가 경고를 띄운다.
- 작가 가이드는 `Docs/`에 있다 `(TBD)`. 문법이 바뀌면 같이 고친다.
- **Yarn 함정**
  - 나레이션 줄의 첫 `:` 앞부분은 화자 이름이 된다. 문장을 바꾸거나 `\:`로 쓴다.
  - `#`은 태그가 시작되는 기호다. 그 뒤가 잘리니 `\#`로 쓴다.
  - `[ ]`는 마크업, `{ }`는 변수다. 대사에 쓰려면 `\[`, `\{`로 쓴다.
  - 선언하지 않은 변수는 기본값이 없어서 조건문이 의도대로 동작하지 않는다. 변수는 `<<declare>>`로 선언한다.

## Architecture

`(TBD: 단계가 진행될 때마다 채운다)`
- **씬**: 모드 단위다. `Title`, `Story` (4-2, 4-4), `Exploration`, `Battle` `(TBD)`. 에피소드는 `Episodes.csv`의 `start_node`로 구분하고, 전용 씬이 필요한 에피소드만 `unity_scene` 열에 씬 이름을 쓴다 (Story 씬을 복제해서 만들고 Build Settings에 추가).
- **에피소드 흐름** (`Scripts/Runtime/Story/`)
  - `EpisodeFlow` (정적): 현재 에피소드, 씬 로드. **에피소드를 시작할 때는 항상 씬을 새로 로드한다** (Story → Story도). DialogueRunner를 중간에 멈출 필요가 없다.
  - `StoryDirector`: 씬이 뜨면 현재 에피소드의 `start_node`를 실행한다. 대사가 끝나면 `next_episode`로, 비어 있으면 타이틀로 간다.
  - `<<episode X>>`: `EpisodeFlow.TryGo` 후, 씬이 바뀌어 토큰이 취소될 때까지 기다린다 (다음 대사가 보이지 않게).
  - `GameVariables`: Yarn 변수 저장소를 `DontDestroyOnLoad`에 둔다. 씬을 다시 로드해도 변수가 유지되고, 타이틀에서 새로 시작할 때만 비운다.
  - `StoryPresenter` (`DialoguePresenterBase`): 이름표(캐릭터 색), 타이핑 효과(진행 중 클릭하면 끝까지 표시), 나레이션 모드(이름표 숨김, 본문 색), `#face:` 태그, 말하는 사람 강조, 선택지 (클릭 또는 숫자 키). 대사창과 무대의 `raycastTarget`은 꺼 둔다 (버튼 위 클릭만 넘기기에서 제외하려고). 노드에 들어갈 때 `NodeEntered` 이벤트를 낸다.
  - `StoryStage`: 무대 (배경 2장 크로스페이드, 스탠딩, CG, 비네트, 와이프, 플래시, 페이드, 흔들림). 세이브용 `GetState`/`ApplyState`.
  - `SaveSystem`: 자동 저장(슬롯 0. 노드에 들어갈 때마다, 탐색은 장소에 들어갈 때, 지도, 조사 후)과 수동 슬롯 1~7(썸네일 PNG). 에피소드, 노드(또는 장소), 변수, 무대, BGM을 저장한다. 수동 저장은 마지막 자동 저장 시점(`LastSnapshot`)을 복사하므로, **불러오면 그 노드(장소)의 처음부터** 시작한다. 전투 중에는 저장할 수 없다.
  - `EpisodeFlow.Resume(save)`: 이어하기, 슬롯 불러오기, 핫리로드가 모두 이 함수로 들어온다 (변수, 대사 기록, 스킵 초기화 후 씬 로드).
  - `DevOverlay`: 에피소드, 노드, 최근 명령을 왼쪽 위에 표시한다. F1로 숨기고, 메뉴가 열려 있으면 저절로 숨는다(썸네일에도 안 찍힘). F5는 지금 노드 다시 시작. Development Build가 아니면 꺼진다.
- **UI** (`ui` 브랜치, 2026-11-18. 다른 AI가 만든 HTML 시안 `dystopia-vn-ui.html`을 옮긴 임시 UI. 디스토피아 톤: 짙은 검정, 페리윙클 테두리, 시안 강조, 앰버 경고, 모노 라벨, CRT 스캔라인)
  - 색은 `Runtime/UI/UITheme`, 부품은 `Editor/UIKit`(깎인 패널, 버튼, 아이콘 버튼, 창, 슬라이더, 스위치, 화면 효과), 배치는 `Editor/DialogueSetup`. 그림은 `Art/UI`(`Editor/UIArtGenerator`가 만든다).
  - 폰트: 한글은 에이투지체 (본문 Medium = TMP 기본, 이름/제목 Bold = `UIKit.Text(..., bold: true)`), 영문 라벨 IBM Plex Mono(`UIKit.Mono`, 섞인 한글은 Medium), 제목 Michroma(`UIKit.Display`, 섞인 한글은 Bold). 창 제목 "SETTINGS 설정"은 `ConfirmDialog.WindowTitle`.
  - 실행 중 부품(`Runtime/UI/`): `UIPanelAnim`(창 열기/닫기: 바탕 페이드 + 창이 올라오며 커짐 + 자식 차례로 페이드), `UIHoverFx`(마우스 올림: 나타나기, 밀리기, 색, 테두리, 자간), `UIToggleSwitch`, `ConfirmDialog`(예/아니오, 씬마다 하나), `UIToast`(아래 알림), `ScreenFx`(스캔라인, 노이즈, 비네트, 육각형, 먼지), `HudPanel`(왼쪽 위 REC + 에피소드). 메뉴는 일시정지 중에 열리므로 애니메이션은 모두 실제 시간(`UIAnim.Run`).
  - 대사창: 아래에서 올라오고 끝나면 내려간다. 이름표 = [식별 칩] + 육각 점 + 이름 (칩은 `Characters.csv`의 `name_tag`가 있을 때만), 왼쪽 막대는 화자 색, LOG 번호, 타이핑 커서, CLICK ▸. 오른쪽 위 AUTO/SKIP/LOG(대사 중에만)/MENU.
  - 설정: 텍스트 속도, 오토 속도, 읽은 대사만 스킵, 스캔라인, 노이즈, 화면 흔들림(끄면 `<<shake>>`와 전투 흔들림 없음), 전체 화면, BGM, 효과음.
  - 시안의 HUD 중 신호/배터리 수치는 세계관 설정이 필요해서 뺐다 (비디님 확인 후). 창 위의 영문 문구(SIGNAL INTERRUPTED 등)는 시안 그대로의 임시 문구다.
- **편의 기능** (4-7, `Scripts/Runtime/Story/`, `Menus/`)
  - `DialogueModes`: 오토(A, 설정의 오토 속도), 스킵(Tab, **읽은 대사만**. 처음 보는 대사, 선택지, 대화 끝, 씬 전환에서 멈춤), 빨리감기(Ctrl 누르는 동안, 안 읽은 대사도. 선택지에서 멈춤). 스킵 중에는 `GamePause.SetSpeed(6)`으로 트윈과 `<<wait>>`도 빨라진다. 오토는 씬이 바뀌어도 유지, 타이틀에서 꺼짐.
  - `ReadLines`: 읽은 줄 = Yarn 줄 id + 본문 해시. 작가가 대사를 고치면 그 줄은 다시 "안 읽음"이 된다. 게임 전체에 하나 (`read_lines.json`).
  - `Backlog`(내용, 최근 300개, 씬이 바뀌어도 유지, 새 게임/불러오기에서 비움) + `BacklogPanel`(L, 휠 위로, 로그 버튼, 일시정지 메뉴의 "대사 기록"). 닫기: 오른쪽 클릭, L, Esc, 맨 아래에서 휠 아래로.
  - `DialogueQuickMenu`: 대사창 오른쪽 위의 로그/오토/스킵 버튼과 단축키. 대사창의 자식이라 대사가 없으면 숨는다.
  - `SaveSlotPanel` + `SaveSlotCard`: 타이틀의 불러오기, 일시정지의 저장/불러오기. 덮어쓰기와 게임 중 불러오기는 한 번 더 눌러야 실행된다. 썸네일은 일시정지 메뉴를 열기 직전에 `Core/ScreenThumbnail`로 찍는다 (0.3초 안에 못 찍으면 썸네일 없이 연다).
  - `DialogueTest`: 노드 테스트(`Run`: 노드 이름으로 에피소드를 추측해서 그 씬에서 실행, 못 찾으면 임시 에피소드 `_NodeTest`로 Story 씬에서 실행하고 끝나면 타이틀)와 핫리로드(`ReloadCurrent`: `LastSnapshot`으로 `Resume`. 씬을 다시 로드하므로 새로 컴파일된 Yarn이 들어온다). 에디터 쪽은 `Editor/DialogueTestRunnerWindow`, `Editor/YarnProjectAutoRefresh`(플레이 중 `.yarn` 수정 시각을 1초마다 보고 직접 임포트 → Yarn 프로젝트가 다시 컴파일되면 게임에 알림).
  - 일시정지 메뉴: 계속하기, 대사 기록(전투 씬에는 없음), 저장, 불러오기, 설정, 타이틀로, 게임 종료. Esc는 위에 열린 것부터 닫는다 (슬롯 → 설정 → 대사 기록 → 메뉴).
- **공용** (`Scripts/Runtime/Core/`)
  - `CsvTable`: BOM, 따옴표, CRLF 처리, 4행부터 데이터. 4-5 임포터도 이것을 쓴다.
  - `AssetCatalog` (`Resources/AssetCatalog.asset`): 이름 → 그림/소리/캐릭터. `Editor/AssetCatalogBuilder`가 폴더를 감시해서 자동으로 채운다. 캐릭터 = `Data/Characters.csv` (`character_id, yarn_name, name_color`) + `Art/Characters/{id}/{id}_{표정}.png`.
  - `AudioManager` (DontDestroyOnLoad): BGM 크로스페이드, 효과음. `ScreenFader` (DontDestroyOnLoad): 씬 전환 페이드.
  - `GameSettings` (PlayerPrefs: 음량, 텍스트 속도 1~10, 창 모드), `GamePause` (timeScale 0, 대사 입력 차단), `Tween` (DOTween 대신 쓰는 Awaitable 트윈), `UISkew` (UI 평행사변형).
- **메뉴** (`Scripts/Runtime/Menus/`): `TitleMenu` (처음부터, 이어하기, 설정, 종료, 개발용 에피소드 선택), `PauseMenu` (Esc 또는 오른쪽 위 버튼), `SettingsPanel` (타이틀과 일시정지 공용).
- **탐색** (`Scripts/Runtime/Exploration/`, Exploration 씬): 지도 → 장소 → 오브젝트 클릭 → Yarn 노드 → 탐색으로 복귀. 대사 UI와 무대는 Story와 같은 것(`BuildDialogueScene`).
  - 데이터: `Exploration_Location.csv` (location_id, display_name, background, enter_node, unlock_if), `Exploration_Object.csv` (object_id, location_id, display_name, node, show_if, once, area, encounter_id) → `Resources/ExplorationData.asset`
  - **클릭 영역은 문서의 장소 프리팹 대신 CSV `area` 열**(`x y 너비 높이`, 1920x1080, 왼쪽 위 기준)로 정한다. 개발 빌드에서는 영역이 옅게 보인다.
  - 조건(`unlock_if`, `show_if`)은 Yarn식 (`Story/YarnCondition.cs`: `$a and not $b`, `$day >= 2`, `==`, 괄호). 선언 초기값도 읽는다.
  - 자동 변수: `$오브젝트id_seen`(대사가 끝나면), `$장소id_visited`(처음 들어갈 때). 임포터가 `Dialogue/_Generated/Exploration.yarn`에 선언을 만든다 (다른 파일에서 또 declare하면 중복 오류).
  - enter_node는 처음 들어갈 때 한 번만. once 오브젝트는 조사 후 사라짐. encounter_id가 있으면 대사 뒤 전투.
  - 에피소드의 unity_scene이 Exploration이면 start_node를 먼저 실행하고 지도를 보여 준다. 나갈 때는 대사에서 `<<episode>>`, `<<explore>>`, `<<battle>>`, `<<title>>`.
  - 세이브: 장소에 들어갈 때와 조사 후 (scene=Exploration, location). 이어하기는 그 장소로.
- **전투** (`Scripts/Runtime/Battle/`, Battle 씬, 문서 02)
  - `BattleCore`: 순수 C#. `Start()` → `RunRound(아군 행동)` → `TakeEvents()`. 아군 행동이 null이면 `Rules.AllyAI`가 고른다 (자동 전투, 시뮬레이터). 난수는 `System.Random(seed)` 하나.
  - 교체 가능한 규칙 (`BattleRules`): `ITurnOrderRule`(속도순), `IDamageFormula`(포켓몬식), `IWinCondition`(전멸), `IActionResolver`(순서대로), `IUnitAI`(가중치 랜덤, 탐욕). 스탯 이름(hp, atk, def, spd), 출전 인원 3, 라운드 제한 99도 여기. `TODO(규칙 대기)`
  - 데이터: `Battle_Stat`, `Battle_Unit`(스탯 열 = stat_id), `Battle_Skill`, `Battle_Status`, `Battle_TypeChart`, `Battle_Encounter` → `Resources/BattleDatabase.asset`
  - 효과 문법: `Damage(power)` `FixedDamage(n)` `PercentDamage(n)` `Heal(n%)` `ApplyStatus(id,턴,확률%)` `RemoveStatus(id)` `ModifyStat(stat,단계)` `ChangeGauge(id,양)` `SkipTurn()`을 `;`로 잇는다. 앞에 `self:`(사용자), `other:`(OnHit의 공격자). 쉼표가 있으니 CSV 칸을 따옴표로 감싼다.
  - 스킬 비용 = 게이지(`cost_kind`), 게이지 처음 값은 같은 이름의 스탯. 상태이상 트리거: turn_start, turn_end, on_hit, on_act.
  - `BattleController` + `BattleUnitView`: 이벤트를 순서대로 재생한다. 스킬은 클릭 또는 숫자 키, 대상은 클릭, 오른쪽 클릭이나 뒤로 버튼으로 취소. 개발 빌드에 자동 전투 버튼.
  - 화면 (`ui` 브랜치, 다른 AI의 시안 `battle.html`을 3 대 3 뼈대에 맞게 옮김. **전투 기획 전의 UI 틀이라 규칙이 오면 바뀔 수 있다**): 위 = HUD + ROUND/조우 칩, 행동 순서 줄(속도순, 지금 행동 강조), LOG/MENU. 가운데 = 적 카드(그림 큼), 아군 카드. 아래 = 전투 기록 한 줄 + 명령 창(스킬 카드 4칸: 타입 칩, 이름, 설명, 비용, 숫자 키 / 능력치, 전투 기록, 뒤로, 자동 / 행동 중에는 COMMAND LOCK 덮개).
  - 연출: 시작 배너(ENGAGE)와 카드 등장, 라운드 배너, 돌진, 피격(그림 흰 번쩍임 + 카드 붉은 기 + 밀려남 + 데미지 숫자 튀어오름), HP 붉은 잔상이 늦게 따라옴, 세게 맞으면 화면 흔들림 + 경직, 치명타 흰 플래시, 아군 피격 시 화면 가장자리 붉게, CRITICAL/WEAK POINT/RESISTED, 쓰러짐(바래며 가라앉음), 결과 창(VICTORY/DEFEAT, 라운드, 준 피해, 남은 HP).
  - `BattleStatusPanel`(능력치 창): 카드 클릭(또는 오른쪽 클릭), 능력치 버튼, I 키. 능력치 줄은 `Battle_Stat.csv`의 행마다 만들어진다 (기본값 → 지금 값, 단계, 전체 유닛 최고값 기준 막대). 상태이상, 상성(약점/내성, `Battle_TypeChart.csv`), 스킬.
  - `BattleLog`: 전투 기록. 대사 기록 창(`BacklogPanel`, `battleSource`)을 그대로 쓴다. 스킬 타입 색(보조/무속성/속성)은 임시라 기획이 오면 데이터로 뺀다.
  - 패배 처리 `lose_rule`: gameover(타이틀로), retry(다시), continue(lose_node). 전투 그림: `Art/Battle/{portrait_id}.png`, 없으면 같은 id 캐릭터의 기본 표정.
  - 더미 데이터(아군 3, 적 3, 스킬 12, 상태이상 3)는 세계관 고유명사를 쓰지 않는다. 비디님의 양식이 오면 CSV와 규칙 클래스를 바꾼다.
- **데이터 테이블**: `Assets/_Project/Data/*.csv`, UTF-8 BOM, LF. 1행은 컬럼명, 2행은 설명, 3행은 허용값이다. id는 영어로 쓴다.

## Gotchas

- **OneDrive 밖, 영문 경로**에 프로젝트를 둔다. OneDrive 동기화는 Library 잠금과 지연을, 한글 경로는 빌드 도구 문제를 일으킨다.
- TMP Settings에서 **"Use Modern Hangul Line Breaking Rules"를 켠다.** TMP Essential Resources를 다시 임포트하면 꺼지니 `1단계 기본 세팅 적용` 메뉴를 다시 실행한다.
- 한글 폰트는 **에이투지체** (상업 이용 가능 무료 폰트, `Assets/_Project/Art/Fonts/A2Z/A2Z-Medium.ttf`, `A2Z-Bold.ttf`. 2026-11-18 Pretendard에서 바꿈)다. TMP 기본 폰트는 `A2Z-Medium SDF` (Dynamic, 2048 멀티 아틀라스)이고, `<b>`는 `A2Z-Bold SDF`를 쓴다. Pretendard 폴더는 남아 있지만 쓰지 않는다. UI 영문 폰트 IBM Plex Mono, Michroma(OFL, `Art/Fonts/`)도 Dynamic이라 커밋 전에 같이 비워진다. 폰트 설정은 `UIArtGenerator.EnsureFonts`(씬 생성이 부름)와 `ProjectSetup`의 상수에 있다.
- TMP `<font="이름">` 태그는 폰트 에셋이 Resources 안에 있어야 동작한다. 한 줄에 영문 폰트와 한글을 섞을 때는 영문 폰트 에셋의 fallback(에이투지체)에 맡긴다.
- 에셋 파일 이름은 영어로 한다 (받은 한글 파일 이름은 `.meta`와 같이 옮겨서 바꾼다. GUID가 유지된다).
- Git LFS는 쓰지 않는다. `.gitattributes`에서 이미지, 오디오, 폰트는 `binary`다.
- Dynamic TMP 폰트 에셋은 플레이하는 동안 커진다. 커밋 전에 비우거나, Static 아틀라스(KS X 1001 2350자)로 굽는다.
- 에디터 도구의 네임스페이스는 `.EditorTools`로 한다. `.Editor`로 끝나면 `: Editor`가 `UnityEditor.Editor` 대신 그 네임스페이스로 해석된다.
- Unity YAML에서 한글 오브젝트 이름은 유니코드 이스케이프(`\uXXXX`)로 저장된다. 프리팹을 한글로 grep하면 찾지 못한다.
- 사용자는 파섹으로 원격 접속한다. UI와 씬 구성은 **에디터 스크립트로 생성**하는 것을 기본으로 하되, 에디터에서 손으로 하는 편이 나으면 그렇게 해도 된다 (사용자가 2026-11-18 `01_프로젝트환경구축.md`에 메모).
- 버튼을 클릭하면 EventSystem의 선택 상태로 남아서, 대사 넘기기(Space/Enter)가 그 버튼까지 누른다. 대사 중에 보이는 버튼은 누른 뒤 `EventSystem.current.SetSelectedGameObject(null)`을 한다 (`DialogueQuickMenu`).
- **새 .yarn 파일은 Yarn Project에 저절로 안 들어간다** (Yarn Spinner 3.3.2. 기존 파일 수정은 반영됨). `Editor/YarnProjectAutoRefresh.cs`가 Dialogue 폴더의 .yarn 추가/삭제/이동 때 프로젝트를 다시 임포트한다.
- ScriptableObject 클래스는 **클래스 이름과 같은 파일**에 둔다. 다른 파일에 있으면 에셋이 스크립트를 잃는다 (`m_Script: {fileID: 0}`).
- `EditorApplication.delayCall`은 에디터에 포커스가 없으면 돌지 않을 때가 있다. 리소스 목록과 CSV 자동 갱신은 Unity로 돌아오면 돈다. 꼭 바로 해야 하는 것은 후처리기 안에서 직접 한다.
- UI는 선형 색공간에서 섞인다. **밝은 반투명(선, 강조, 노이즈)은 브라우저보다 훨씬 진하게, 어두운 반투명 판은 훨씬 비쳐 보인다.** HTML/CSS 값을 옮길 때 판은 알파 0.98 이상, 선과 강조는 절반쯤으로, 노이즈는 흰색 대신 검은색으로 쓴다 (`UITheme` 참고). 자식 Image는 항상 부모 위에 그려지니, 카드 테두리는 `Outline` 효과나 테두리 그림으로 만든다.
- uGUI `Slider`는 Fill과 Handle을 부모(FillArea, HandleArea) 높이에 맞춰 늘린다. 얇은 막대는 부모 높이를 막대 두께로, 손잡이는 부모 높이를 0으로 둔다 (`UIKit.Slider`).
- 레이아웃 그룹 안의 자식은 위치를 애니메이션하면 레이아웃과 싸운다. 차례로 나타나기는 CanvasGroup 알파만 바꾸고, 움직임은 레이아웃 밖의 자식(Content 등)에 준다. 카드를 흐리게 하는 알파도 등장 애니메이션과 겹치지 않게 안쪽 CanvasGroup에 둔다.
- **Yarn 명령의 float 기본값**: Yarn Spinner 3.3.2 소스 제너레이터가 `float time = 0.6f`를 `0.6`(double)로 옮겨 써서 컴파일 에러가 난다. 소수 기본값은 `-1`(`StoryCommands.Auto`)로 두고 메서드 안에서 바꾼다. 정수 기본값은 괜찮다.
- 생성된 Yarn 코드를 보려면 복사본 csproj를 `dotnet build -p:EmitCompilerGeneratedFiles=true -p:CompilerGeneratedFilesOutputPath=gen`으로 빌드한다.
- **MCP for Unity**는 프로젝트 `.mcp.json` (`http://127.0.0.1:8080/mcp`)으로 연결한다. 서버는 Unity의 `Window > MCP for Unity`에서 시작한다 (에디터 시작 시 자동 실행 옵션이 꺼져 있으면 Unity를 재시작할 때마다 다시 눌러야 한다). VS Code 확장은 프로젝트 경로를 `c:/...`(소문자)로, Configure 버튼은 `~/.claude.json`에 `C:/...`(대문자)로 저장해서 서로 안 맞는다.
- **MCP로 플레이 테스트할 때**
  - 에디터가 포커스를 잃으면 플레이 루프가 멈춘다 (`runInBackground` false). 플레이 중에 `Application.runInBackground = true`를 실행한다 (런타임 값이라 설정은 바뀌지 않는다).
  - 포커스가 없으면 Input System이 키와 마우스 입력(시뮬레이션 포함)을 게임에 보내지 않는다. 입력 확인은 사용자 플레이로 한다.
  - **`InputSystem.settings`를 런타임에 바꾸거나 새 인스턴스로 교체하지 않는다.** 도메인 리로드 뒤에도 Input System이 깨지고, 복구하면 장치 목록이 사라져서 에디터를 재시작해야 한다.
  - `manage_camera` screenshot은 `Assets/Screenshots/`에 저장된다. 테스트 후 지운다. `execute_code`는 `AssetDatabase.DeleteAsset`을 막으므로 파일 시스템에서 지우고 Refresh한다.
  - 테스트로 만든 세이브 슬롯과 `read_lines.json`은 사용자 PC의 세이브 폴더에 남는다. 테스트 후 지운다.
  - 플레이하면 Dynamic 폰트 에셋이 커진다. 테스트 후 `Dynamic 폰트 비우기` 메뉴를 실행한다.

## Conventions

- 게임 콘텐츠는 `Assets/_Project/`에 둔다 (`Dialogue`, `Art`, `Audio`, `Data`, `Prefabs`, `Scenes`, `ScriptableObjects`, `Scripts/Runtime`, `Scripts/Editor`). 문서는 `Docs/` (작가용), `Docs/Dev/` (개발용), 개발 도구는 `Tools/`에 둔다.
- 빈 폴더는 `.gitkeep`으로 유지한다 (Unity는 `.`으로 시작하는 파일을 무시한다). 폴더에 실제 파일이 생기면 지운다.
- 코드 네임스페이스는 `ProjectVita.*`이고 (에디터 도구는 `ProjectVita.EditorTools`), 주석은 한국어로 단다.
- 임시 그림은 단색 PNG와 이름표로 만든다. 최종 아트는 같은 파일명으로 덮어쓴다. 다른 게임이나 이전 프로젝트의 그림은 넣지 않는다.
- 커밋 메시지는 `type : message` (`init`, `feat`, `fix`, `minor`)다.
