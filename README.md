# 중카 번역기 (Game OCR Translator)

> 중국 카트라이더 화면에서 지정한 영역을 OCR로 읽고 한국어로 번역해 보여주는 Windows용 오버레이 번역기입니다.

![중카 번역기 UI](docs/ui-preview.png)

## ✨ 주요 기능

- 실행 중인 게임 창 선택 및 번역 영역 지정
- Windows 내장 OCR 기반 중국어 간체/일본어 화면 문자 인식
- DeepL API 또는 Google 번역 방식으로 한국어 번역 표시
- 선택 영역 오버레이 또는 별도 결과 창 표시
- 채팅 중복/노이즈 필터와 사용자 사전 기반 API 호출 절감
- 오버레이 글꼴, 크기, 색상, 테두리, 투명도 조정

## ⚙️ 실행 전 준비

1. Windows 10/11에서 실행합니다.
2. Windows 설정에서 OCR에 사용할 언어 팩을 설치합니다.
   - 중국어 간체 화면 인식: 중국어(간체) 언어 팩
   - 일본어 화면 인식: 일본어 언어 팩
3. DeepL을 사용할 경우 DeepL API 키를 발급받아 앱 설정에 입력합니다.
4. 게임은 전체화면보다 창 모드 또는 테두리 없는 창 모드를 권장합니다.

## 📦 다운로드 및 실행

1. [GitHub Releases](https://github.com/kwon1h/jung-ka-translator/releases)에서 최신 `GameOverlayTranslator-win-x64.zip`을 다운로드합니다.
2. 원하는 폴더에 압축을 풉니다.
3. `GameOverlayTranslator.App.exe`를 실행합니다.

## 🕹️ 사용법

1. 게임 창을 선택합니다.
2. `영역 선택 (F9)`으로 번역할 화면 영역을 드래그합니다.
3. OCR 언어와 번역 언어를 선택합니다.
4. 번역 표시 방식을 선택합니다.
   - 선택 영역 오버레이
   - 별도 결과 창
5. `번역 시작 (F8)`을 누릅니다.

단축키:

- `F8`: 번역 시작/정지
- `F9`: 번역 영역 다시 선택

## 📕 사용자 사전

레포지토리에 사용자 사전을 포함했습니다.

- 파일: [docs/user_dictionary.csv](docs/user_dictionary.csv)
- 컬럼: `Source,Target,Category`

앱은 배포 시 포함되는 `Assets/user_dictionary.csv`를 기본 사전으로 읽고, 실행 중 사용자 사전은 아래 위치에 저장합니다.

```text
%LocalAppData%\GameOverlayTranslator\user_dictionary.csv
```

기본 사전과 사용자가 추가한 항목은 앱에서 병합해 사용합니다. 기존 JSON 사전만 있는 경우 첫 실행 때 CSV로 자동 이전됩니다.

## 🧪 로그

진단 로그는 아래 폴더에 저장됩니다.

```text
%LocalAppData%\GameOverlayTranslator\logs\
```

OCR 원문, 필터 결과, 번역 실패 원인 등을 확인할 때 사용합니다.

## 🛠️ 개발

필요 조건:

- .NET 8 SDK
- Windows 10 SDK `10.0.19041.0` 이상

명령:

```powershell
dotnet restore GameOverlayTranslator.sln --configfile NuGet.Config
dotnet build GameOverlayTranslator.sln --configfile NuGet.Config
dotnet run --project src\GameOverlayTranslator.App\GameOverlayTranslator.App.csproj
dotnet run --project tests\GameOverlayTranslator.RegressionTests\GameOverlayTranslator.RegressionTests.csproj
```

## 📄 라이선스

MIT License. 자세한 내용은 [LICENSE](LICENSE)를 참고하세요.
