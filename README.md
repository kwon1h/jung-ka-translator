# 중카 번역기 (Game OCR Translator)

> **중국 카트라이더(중카) 전용 실시간 화면 OCR 번역기**
> 
> 게임 화면의 채팅 영역을 실시간으로 캡처/분석하여 번역한 뒤, 게임 화면 위에 자막처럼 깔끔한 오버레이로 띄워주거나 별도의 결과 창에 표시해 주는 Windows 데스크톱 프로그램입니다.

---

## ✨ 주요 특징 (Key Features)

* **실시간 자막 오버레이**: 게임 화면 위에 투명창을 얹어, 검은 외곽선이 포함된 흰색 글씨(시인성 극대화)로 실시간 번역본을 보여줍니다.
* **Windows 내장 OCR 사용**: 별도의 무거운 외부 엔진 없이 Windows 10/11 기본 OCR 엔진(`Windows.Media.Ocr`)을 활용하여 가볍고 빠르게 문자를 추출합니다.
* **DeepL 번역 연동**: 최고 품질의 DeepL API(무료/유료 플랜 지원)를 연동하여 번역을 수행합니다. API 키는 Windows DPAPI로 안전하게 암호화되어 개인 PC 내에 저장됩니다.
* **채팅 최적화 필터 및 중복 제거**:
  * **중복/유사 채팅 필터링**: 화면이 갱신될 때마다 동일한 대화가 중복 번역되어 API 할당량을 낭비하지 않도록 Jaccard 유사도 기반으로 걸러냅니다.
  * **OCR 오타 보정 및 교체**: 이전 프레임에서 글자가 일부 깨져서 번역되었다가, 이후 프레임에서 온전하게 인식되면 기존 번역본을 더 높은 품질의 결과로 교체(Replace)합니다.
  * **다중 라인 분할**: Windows OCR이 여러 줄의 채팅을 하나의 라인으로 뭉쳐서 인식할 때, 이를 유저별(`유저명: 메시지`)로 파싱하여 쪼개어 번역합니다.
  * **노이즈 필터**: 의미 없는 특수문자나 OCR 인식 오류로 생긴 노이즈 덩어리는 번역 요청에서 제외합니다.

---

## 🚀 다운로드 및 실행 방법

본 프로그램은 설치 과정이 전혀 필요 없는 **단일 실행 파일(Portable)**로 제공됩니다.

1. [GitHub Releases](https://github.com/kwon1h/jung-ka-translator/releases) 페이지로 이동합니다.
2. 가장 최신 버전의 **`GameOverlayTranslator-win-x64.zip`** 파일을 다운로드합니다.
3. 압축을 원하는 안전한 경로에 해제합니다.
4. 폴더 내의 **`GameOverlayTranslator.App.exe`**를 실행하면 즉시 시작됩니다.

---

## 🛠️ 실행 전 필수 설정 (Prerequisites)

> [!IMPORTANT]
> 프로그램이 정상 작동하려면 다음 두 가지 사전 설정이 반드시 완료되어야 합니다.

### 1. Windows OCR 언어 팩 설치
중국어 또는 일본어 화면을 인식하려면 Windows OS 자체에 해당 언어 팩이 깔려 있어야 합니다.

1. Windows **설정** (`Win + I`) -> **시간 및 언어** 메뉴로 이동합니다.
2. **언어 및 지역**을 선택합니다.
3. **언어 추가** 버튼을 눌러 번역할 언어(예: 중국어 간체 `中文(简体, 중국)`, 일본어 `日本語`)를 추가합니다.
4. 설치 시 **언어 팩(Language Pack)** 또는 **기본 입력/텍스트 음성 변환(Basic typing)** 항목이 체크되었는지 확인하고 설치를 완료합니다.
5. (설치 완료 후 프로그램을 켜두었다면 재시작해 주세요.)

### 2. DeepL API 키 발급 및 입력
실시간 번역 API 호출을 위해 DeepL API 키가 필요합니다.

1. [DeepL API 등록 페이지](https://www.deepl.com/pro-api)에 접속하여 회원 가입을 진행합니다. (무료 플랜인 'DeepL API Free'를 선택하시면 매월 50만 자까지 무료 번역이 가능합니다. 가입 시 본인 인증을 위해 카드를 등록하지만 요금은 청구되지 않습니다.)
2. 가입 완료 후, [DeepL API 키 관리 페이지 (https://www.deepl.com/ko/your-account/keys)](https://www.deepl.com/ko/your-account/keys)에 접속합니다.
3. 페이지 하단의 **API 인증 키(Authentication Key)** 목록에서 키를 복사합니다.
4. 중카 번역기 프로그램 메인 화면에서 복사한 키를 붙여넣은 후 **저장** 버튼을 누릅니다. (입력된 키는 Windows DPAPI로 안전하게 암호화되어 보관됩니다.)

---

## 📖 사용 방법

```
1. 대상 게임 실행 (테두리 없는 창 모드 혹은 창 모드 권장)
2. 번역기에서 대상 '게임 창' 선택 (드롭다운 목록)
3. '영역 지정' 버튼 클릭 (혹은 F9 단축키) ➡️ 게임의 채팅창 영역을 드래그하여 지정
4. 원본 OCR 언어 선택 (기본값: 중국어 간체)
5. '시작' 버튼 클릭 (혹은 F8 단축키) ➡️ 실시간 오버레이 번역 작동!
```

### 💡 유용한 팁
* **화면 모드 전환**: 번역 결과창 하단에서 '결과창 모드'와 '오버레이 모드'를 토글할 수 있습니다.
* **단축키 활용**: 게임 플레이 중 `F8` 키로 실시간 번역을 시작/정지할 수 있고, 채팅창 위치가 바뀌었을 때는 `F9` 키를 눌러 바로 다시 영역을 그릴 수 있습니다.
* **모듈 테스터**: 메인 창의 `모듈 테스터` 버튼을 누르면 창 캡처, OCR 단독 인식, DeepL 번역 기능이 개별적으로 정상 작동하는지 디버깅해볼 수 있습니다.

---

## 💻 빌드 및 개발 (For Developers)

이 앱은 `.NET 8.0` 및 `Windows 10 SDK (10.0.19041)` 환경을 타겟팅합니다. 로컬 빌드 및 실행은 다음과 같이 진행합니다.

```powershell
# NuGet 의존성 복구
dotnet restore GameOverlayTranslator.sln --configfile NuGet.Config

# 솔루션 빌드
dotnet build GameOverlayTranslator.sln --configfile NuGet.Config

# 프로젝트 로컬 실행
dotnet run --project src\GameOverlayTranslator.App\GameOverlayTranslator.App.csproj --configfile NuGet.Config
```

---

## 📜 라이선스 (License)

이 프로젝트는 **MIT 라이선스**하에 배포됩니다. 자세한 내용은 [LICENSE](LICENSE) 파일을 참조하세요.
