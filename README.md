# 중카 번역기

중국 카트라이더 화면의 채팅과 화면 문구를 OCR로 읽어 한국어로 번역하는 Windows용 오버레이 번역기입니다.

![중카 번역기 미리보기](docs/ui-preview.png)

## 기능

- [번역 모드](docs/features/번역모드.md): [채팅 번역](docs/features/번역모드.md#채팅-번역), [전체화면 번역](docs/features/번역모드.md#전체화면-번역), [영역 선택과 제외 영역](docs/features/번역모드.md#영역-선택과-제외-영역)
- [OCR 엔진](docs/features/OCR엔진.md): [Windows OCR](docs/features/OCR엔진.md#windows-ocr), [PaddleOCR(OpenVINO)](docs/features/OCR엔진.md#paddleocropenvino), [엔진 선택 기준](docs/features/OCR엔진.md#엔진-선택-기준)
- [번역 서비스](docs/features/번역서비스.md): [DeepL API](docs/features/번역서비스.md#deepl-api-추천), [Google 비공식 API](docs/features/번역서비스.md#google-번역-비공식-api), [Google Apps Script](docs/features/번역서비스.md#google-apps-script)
- [유저 사전](docs/features/유저사전.md): [직접 추가](docs/features/유저사전.md#직접-추가), [화면 OCR로 채우기](docs/features/유저사전.md#화면-ocr로-채우기), [저장 위치](docs/features/유저사전.md#저장-위치)

전체 목차는 [기능 가이드](docs/FEATURES.md)를 참고하세요.

## 설치

1. [Releases](https://github.com/kwon1h/jung-ka-translator/releases)에서 최신 `GameOverlayTranslator.exe`를 받습니다.
2. 원하는 폴더에 파일을 둡니다.
3. `GameOverlayTranslator.exe`를 실행합니다.

처음 실행할 때 Windows SmartScreen 경고가 나오면 `추가 정보`를 누른 뒤 실행할 수 있습니다.

## 사용법

1. 게임을 창모드 또는 테두리 없는 전체화면으로 실행합니다.
2. 번역할 게임 창을 선택합니다.
3. `채팅 번역` 또는 `전체화면 번역`을 선택합니다.
4. `영역 선택` 버튼 또는 `F9`로 번역할 영역을 지정합니다.
5. 번역 서비스와 OCR 엔진을 확인합니다.
6. `번역 시작` 버튼 또는 `F8`로 번역을 시작하고 정지합니다.

독점 전체화면에서는 오버레이가 보이지 않을 수 있습니다.

## 개발

```powershell
dotnet build src\GameOverlayTranslator.App\GameOverlayTranslator.App.csproj
dotnet run --project tests\GameOverlayTranslator.RegressionTests\GameOverlayTranslator.RegressionTests.csproj
.\scripts\build-release.ps1
```

자세한 내용은 [빌드 가이드](docs/BUILD.md)를 참고하세요.

## License

MIT License. See [LICENSE](LICENSE).
