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

## 🌐 Google Apps Script 번역 API 설정

앱의 `Google Apps Script (무료/개인 웹앱)` 방식은 사용자가 직접 만든 Google Apps Script 웹 앱을 번역 프록시처럼 호출합니다. 앱은 웹 앱 URL로 JSON을 `POST`하고, Apps Script는 `LanguageApp.translate()`로 번역한 뒤 JSON으로 결과를 돌려줍니다. Google 공식 Apps Script 문서에 따르면 `LanguageApp.translate(text, sourceLanguage, targetLanguage)`는 원문 언어와 대상 언어 코드를 받아 번역 문자열을 반환하며, 원문 언어에 빈 문자열을 넣으면 자동 감지를 사용합니다.

### 1. Apps Script 프로젝트 만들기

1. [Google Apps Script](https://script.google.com/)에 접속합니다.
2. `새 프로젝트`를 누릅니다.
3. 기본으로 열리는 `Code.gs` 내용을 아래 코드로 교체합니다.

```javascript
function doPost(e) {
  try {
    const body = JSON.parse(e.postData.contents || '{}');
    const q = body.q;
    const source = body.source || '';
    const target = body.target || 'ko';

    if (Array.isArray(q)) {
      const translatedTexts = q.map(function (text) {
        return LanguageApp.translate(String(text || ''), source, target);
      });

      return jsonResponse({
        translatedTexts: translatedTexts
      });
    }

    const translatedText = LanguageApp.translate(String(q || ''), source, target);
    return jsonResponse({
      translatedText: translatedText
    });
  } catch (error) {
    return jsonResponse({
      error: String(error && error.message ? error.message : error)
    });
  }
}

function jsonResponse(data) {
  return ContentService
    .createTextOutput(JSON.stringify(data))
    .setMimeType(ContentService.MimeType.JSON);
}
```

### 2. 웹 앱으로 배포하기

1. 우측 상단 `배포` > `새 배포`를 누릅니다.
2. `유형 선택`에서 `웹 앱`을 선택합니다.
3. `실행 사용자`는 `나`로 둡니다.
4. `액세스 권한이 있는 사용자`는 앱에서 호출할 수 있도록 `모든 사용자` 또는 `모든 사용자(익명 포함)`으로 설정합니다.
5. `배포`를 누른 뒤 권한 승인 화면이 나오면 Google 계정으로 승인합니다.
6. 생성된 `웹 앱 URL`을 복사합니다. 보통 아래와 같은 형태입니다.

```text
https://script.google.com/macros/s/AKfycb.../exec
```

### 3. 중카 번역기에 URL 입력하기

1. 중카 번역기를 실행합니다.
2. `번역 API 설정`의 `번역 서비스 선택`에서 `Google Apps Script (무료/개인 웹앱)`을 선택합니다.
3. `Google Apps Script 웹 앱 URL` 입력칸에 위에서 복사한 `/exec` URL을 붙여넣습니다.
4. `저장`을 누른 뒤 `번역 시작 (F8)`을 누릅니다.

### 요청/응답 형식

앱은 단일 문장 번역 시 아래 JSON을 보냅니다.

```json
{
  "q": "你好",
  "target": "ko",
  "source": "zh-Hans"
}
```

Apps Script는 아래처럼 `translatedText`를 반환해야 합니다.

```json
{
  "translatedText": "안녕하세요"
}
```

여러 줄을 한 번에 번역할 때는 `q`가 문자열 배열로 전달됩니다.

```json
{
  "q": ["你好", "开始游戏"],
  "target": "ko",
  "source": "zh-Hans"
}
```

이 경우 Apps Script는 `translatedTexts` 배열을 반환해야 합니다.

```json
{
  "translatedTexts": ["안녕하세요", "게임 시작"]
}
```

### 문제 해결

- `Google Apps Script 웹 앱 URL이 설정되지 않았습니다`: 앱 설정에서 웹 앱 URL을 저장하지 않은 상태입니다.
- `Google Web App 번역 요청 실패`: 웹 앱 URL이 잘못됐거나, Apps Script 배포 권한이 외부 호출을 허용하지 않는 상태입니다. URL이 `/exec`로 끝나는지, 배포 접근 권한이 `모든 사용자` 또는 `모든 사용자(익명 포함)`인지 확인합니다.
- `translatedText 속성을 찾을 수 없습니다`: Apps Script 응답 JSON에 `translatedText` 또는 `translatedTexts`가 없습니다. 위 예제 코드와 응답 키 이름이 같은지 확인합니다.
- 스크립트를 수정한 뒤에도 이전 동작이 계속됨: Apps Script는 수정 후 `배포 관리`에서 새 버전으로 다시 배포해야 웹 앱에 반영됩니다.
- 호출량이 많을 때 실패함: Apps Script와 Language 서비스에는 Google 계정별 실행 시간/호출량 제한이 있습니다. 채팅 중복 필터와 사용자 사전을 켜 두면 호출량을 줄일 수 있습니다.

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
