# 중카 번역기 (Game OCR Translator)

중국 카트라이더 PopKart 화면의 채팅 영역을 OCR로 읽고 한국어로 표시하는 Windows 전용 데스크톱 번역기입니다. 목표는 게임 채팅처럼 짧고 반복되는 문장을 빠르게 처리하고, API 사용량을 낭비하지 않는 실시간 번역 흐름입니다.

## 주요 기능

- 실행 중인 게임 창 선택
- 선택한 게임 창 기준 채팅 영역 드래그 지정
- Windows OCR 기반 중국어 간체/일본어 인식
- DeepL API Free 기반 한국어 번역
- 게임 영역 위 선택 영역 오버레이 표시
- 27인치 FHD 기준 가독성 프리셋
- 별도 결과 창 표시
- 게임 고정 UI/채팅 문구용 사용자 사전
- 카테고리별 사용자 사전 관리
- 동일/유사 채팅 중복 필터
- 세션 단위 API 요청량 표시
- 마지막 선택 창과 영역 자동 복원
- 진단 로그와 수동 OCR 파서 테스트

## 실행 전 준비

### 1. Windows OCR 언어팩

중국어 또는 일본어 OCR을 쓰려면 Windows 언어팩이 설치되어 있어야 합니다.

1. Windows 설정을 엽니다.
2. `시간 및 언어` -> `언어 및 지역`으로 이동합니다.
3. 중국어 간체 또는 일본어를 추가합니다.
4. 언어 기능에서 기본 입력 또는 언어팩 설치가 완료되었는지 확인합니다.
5. 앱이 이미 켜져 있었다면 다시 실행합니다.

### 2. DeepL API Free 키

번역 API 호출에는 DeepL API 키가 필요합니다.

1. [DeepL API 페이지](https://www.deepl.com/pro-api)에 접속합니다.
2. DeepL API Free 플랜을 생성합니다.
3. [DeepL 인증 키 페이지](https://www.deepl.com/ko/your-account/keys)에서 키를 복사합니다.
4. 앱의 `DeepL API Free 인증 키` 입력란에 붙여넣고 저장합니다.

API 키는 Windows DPAPI로 현재 사용자 범위에 암호화 저장됩니다. 저장 위치는 `%LocalAppData%\GameOverlayTranslator\deepl-free-auth.key`입니다.

## 사용 방법

1. PopKart를 창모드 또는 전체창모드로 실행합니다.
2. 앱에서 게임 창을 선택합니다.
3. `영역 선택 (F9)`을 눌러 게임 채팅 영역을 드래그합니다.
4. OCR 언어를 선택합니다. 기본값은 중국어 간체입니다.
5. 표시 방식을 선택합니다.
   - `선택 영역 오버레이`: 게임 채팅 영역 위에 번역문 표시
   - `별도 결과 창`: 독립 창에 채팅 결과 표시
6. 오버레이를 쓴다면 가독성 프리셋을 선택합니다.
   - `기본`: 검은 반투명 배경 + 흰 글씨
   - `강조`: 진한 배경 + 노란 글씨
   - `원문 보호`: 배경 없이 흰 테두리 중심
   - `밝은 배경용`: 밝은 게임 화면용 진한 배경
   - `어두운 배경용`: 어두운 게임 화면용 밝은 배경
7. `번역 시작 (F8)`을 누릅니다.

단축키:

- `F8`: 번역 시작/정지
- `F9`: 영역 다시 선택

## 번역 품질과 API 사용 정책

이 앱은 게임 채팅 특성상 사전을 우선합니다.

- 사용자 사전에 정확히 일치하는 문장은 DeepL로 보내지 않습니다.
- 동일한 OCR 문장은 세션 내에서 다시 번역하지 않습니다.
- 같은 유저의 유사 채팅은 최근 캐시로 걸러 API 호출을 줄입니다.
- 번역 품질이 낮은 OCR 결과는 번역하지 않고 필터링하거나 OCR 원문만 표시합니다.
- 번역 실패 시 앱이 종료되지 않고 원문 또는 사전 치환 결과를 표시합니다.

현재 정책과 주의점:

- 사전 부분 치환 후 중국어/일본어가 남지 않은 문장은 DeepL로 보내지 않습니다.
- 화면 번역 실험 모드는 문장 단위가 아니라 UI 텍스트 단위라 API 사용량이 늘 수 있습니다. v1 기본 목표는 채팅 번역입니다.

## 사용자 사전

기본 사전은 다음 범주로 관리합니다.

- 게임 UI 고정어
- 채팅 빠른 답장
- 트랙/모드명
- 아이템/차량 용어

앱 실행 시 기본 사전은 `%LocalAppData%\GameOverlayTranslator\user_dictionary.csv`에 병합됩니다. 사용자가 직접 추가한 항목은 유지됩니다.

CSV 컬럼은 `Source,Target,Category` 순서입니다. 기존 `%LocalAppData%\GameOverlayTranslator\user_dictionary.json` 파일만 있는 경우 첫 실행 때 CSV로 자동 이전됩니다.

사전 탭에서 새 항목을 추가할 때 분류를 함께 지정할 수 있습니다.

## 로그와 디버깅

로그 위치:

```text
%LocalAppData%\GameOverlayTranslator\logs\
```

진단 로그 탭에서는 최근 OCR 원문, 파서 결과, 필터 규칙, 필터 이유를 확인할 수 있습니다. 수동 OCR 텍스트 테스트에는 다음 형식의 샘플을 넣어 파서와 필터를 확인할 수 있습니다.

```text
zuyeong: 快使用天使!
```

## 제한 사항

- v1은 단일 게임 창과 단일 채팅 영역을 기준으로 합니다.
- 독점 전체 화면은 지원 대상이 아닙니다. 창모드 또는 전체창모드를 사용해야 합니다.
- Windows OCR 품질은 게임 해상도, 글자 크기, 배경 투명도, 언어팩 설치 상태에 영향을 받습니다.
- 오버레이는 OCR 캡처 피드백을 줄이기 위해 캡처 직전 숨김 처리를 사용합니다.

## 개발 문서

- [개발 가이드라인](guideline.md)
- [실패 원인 분석](docs/failure-analysis.md)

## 개발

```powershell
dotnet restore GameOverlayTranslator.sln --configfile NuGet.Config
dotnet build GameOverlayTranslator.sln --configfile NuGet.Config
dotnet run --project src\GameOverlayTranslator.App\GameOverlayTranslator.App.csproj
dotnet run --project tests\GameOverlayTranslator.RegressionTests\GameOverlayTranslator.RegressionTests.csproj
```

이 저장소에는 로컬 .NET SDK가 포함될 수 있습니다. 이 경우 기존 작업 방식에 맞춰 `.dotnet\dotnet`을 사용합니다.

## 라이선스

MIT License. 자세한 내용은 [LICENSE](LICENSE)를 확인하세요.
