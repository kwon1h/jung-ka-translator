# Game OCR Translator

Windows desktop application (WPF) for real-time OCR translation of a selected game screen region. It extracts text from game screens (such as chat windows) and translates it using the DeepL API, rendering it as a clean text overlay directly on top of your game or in a separate result window.

지정한 게임 화면 영역을 실시간으로 OCR 인식하여 DeepL API로 번역하고, 인게임 화면에 자막처럼 오버레이하거나 별도의 결과 창에 표시해 주는 Windows 데스크톱(WPF) 프로그램입니다.

---

## 🚀 How to Download & Run / 다운로드 및 실행 방법

This application is distributed as a **portable single-file executable**. You do not need to install .NET runtime or any setup wizard.
본 프로그램은 **포터블 단일 실행 파일**로 빌드되어 제공됩니다. 별도의 설치 프로그램이나 .NET 런타임 수동 설치가 필요 없습니다.

1. Go to the [Releases](https://github.com/) page (link will be active once hosted on GitHub).
2. Download the latest `GameOverlayTranslator-win-x64.zip` file.
3. Extract the ZIP archive anywhere on your computer.
4. Run `GameOverlayTranslator.App.exe`.

1. [Releases](https://github.com/) 페이지에서 최신 버전의 `GameOverlayTranslator-win-x64.zip`을 다운로드합니다.
2. 압축을 원하는 폴더에 풉니다.
3. `GameOverlayTranslator.App.exe` 파일을 실행합니다.

---

## 🛠️ Prerequisites & Setup / 사전 준비 사항 및 설정

### 1. Windows OCR Language Packs (Mandatory / 필수)
The native Windows OCR engine relies on the language packs installed on your system. If you want to recognize Chinese or Japanese, you must install the respective Windows language packs first.
프로그램 내장 OCR은 Windows 기본 OCR 엔진을 사용하므로, 번역하려는 원본 언어(예: 중국어, 일본어 등)의 Windows 언어 팩이 시스템에 반드시 설치되어 있어야 합니다.

* **How to Install / 설치 방법**:
  1. Open Windows **Settings** (`Win + I`) -> Go to **Time & Language** (시간 및 언어).
  2. Select **Language & Region** (언어 및 지역).
  3. Click **Add a language** (언어 추가) and select the source language (e.g. Chinese Simplified `中文(简体)`, Japanese `日本語`).
  4. Ensure you check **Language pack** or **Text-to-speech / Basic typing** when installing.
  5. Restart this application if it was open.

### 2. DeepL API Key (Mandatory / 필수)
To perform translations, you need a free or pro DeepL API Key.
실시간 번역을 수행하려면 DeepL API 키(Free 또는 Pro)가 필요합니다.

1. Register at [DeepL Developer Portal](https://www.deepl.com/pro-api).
2. Get your authentication key (API Key) from your Account page.
3. Enter it in the main window of the app and click **Save**. (The key is safely encrypted on your PC using Windows DPAPI).

1. [DeepL 개발자 포털](https://www.deepl.com/pro-api)에서 회원가입(무료 플랜 제공)합니다.
2. 계정 페이지에서 API 인증 키(Authentication Key)를 확인합니다.
3. 프로그램 메인 화면의 DeepL 키 입력창에 복사한 키를 붙여넣고 저장합니다. (입력된 키는 Windows DPAPI로 암호화되어 안전하게 보관됩니다).

---

## 📖 How to Use / 사용 방법

1. **Select Game Window / 게임 창 선택**: Run your game in windowed or borderless-windowed mode. Select the game process/window title from the application dropdown.
2. **Select Region / 영역 지정**: Click **Select Region** (or press `F9`) and drag a rectangle over the chat or text area in your game.
3. **Choose OCR Language / 언어 선택**: Choose the OCR source language (e.g., Chinese, Japanese).
4. **Start Polling / 번역 시작**: Click **Start** (or press `F8`) to begin real-time translation. Press `F8` again to pause.
5. **Display Mode / 출력 모드**:
   - **Result Window (결과 창)**: Displays translations in a clean chat list style. Hovering over a line shows the raw OCR text.
   - **Overlay Mode (오버레이 모드)**: Overlays transparent, outlined text directly on the selected game screen area.

1. **대상 게임 선택**: 게임을 창 모드나 테두리 없는 창 모드로 실행합니다. 앱의 창 선택 목록에서 대상 게임을 선택합니다.
2. **영역 선택**: **영역 지정** 버튼을 클릭(또는 `F9` 단축키 입력)하고, 게임 화면 내의 채팅 영역을 마우스로 드래그하여 지정합니다.
3. **언어 지정**: 캡처 대상 텍스트의 언어(OCR 언어)를 선택합니다.
4. **번역 시작**: **시작** 버튼을 누르거나 `F8` 단축키를 눌러 번역을 시작합니다. 정지하려면 다시 `F8`을 누릅니다.
5. **출력 방식 변경**:
   - **일반 결과 창**: 메신저 대화처럼 실시간 번역본이 아래로 쌓입니다. 마우스를 올리면 원문을 볼 수 있습니다.
   - **오버레이 모드**: 게임 채팅창 위치에 투명 자막 오버레이를 얹어, 인게임 자막처럼 번역 텍스트를 검정 외곽선이 포함된 흰 글씨로 보여줍니다.

---

## ⚙️ Advanced Settings & Filters / 필터 및 동작 상세

The app is tuned for game chats (typically in `speaker: message` structure) to minimize API costs and screen clutter:
* **Duplicate/Fuzzy Suppression**: Filters out duplicate text from consecutive frames. If OCR quality improves in a subsequent frame, it replaces the existing translation instead of adding duplicates.
* **Format Splitter**: If multiple lines of chat are scanned together, the app splits them based on username formats before translating.
* **Quality Filter**: Rejects fragmented OCR noise, overly short fragments, or garbage characters.

---

## 🛠️ Build & Run (For Developers)

The app targets `net8.0-windows10.0.19041.0`. With a .NET 8 SDK on `PATH`:

```powershell
# Restore NuGet dependencies
dotnet restore GameOverlayTranslator.sln --configfile NuGet.Config

# Build the solution
dotnet build GameOverlayTranslator.sln --configfile NuGet.Config

# Run the project
dotnet run --project src\GameOverlayTranslator.App\GameOverlayTranslator.App.csproj --configfile NuGet.Config
```

---

## 📜 License / 라이선스

This project is licensed under the **MIT License**. See the [LICENSE](LICENSE) file for details.

본 프로젝트는 **MIT 라이선스**에 따라 배포됩니다. 자세한 내용은 [LICENSE](LICENSE) 파일을 참고하세요.
