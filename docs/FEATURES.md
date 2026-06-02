# 기능 가이드

중카 번역기는 선택한 Windows 게임 창을 캡처하고, OCR로 읽은 텍스트를 한국어로 번역해 별도 창 또는 화면 오버레이로 표시합니다. 이 문서는 기능별 상세 가이드로 이동하기 위한 목차입니다.

![중카 번역기 메인 화면](ui-preview.png)

## 문서 목차

| 기능 | 상세 문서 | 주요 구역 |
| --- | --- | --- |
| 번역 모드 | [번역모드.md](features/번역모드.md) | [채팅 번역](features/번역모드.md#채팅-번역), [전체화면 번역](features/번역모드.md#전체화면-번역), [영역 선택과 제외 영역](features/번역모드.md#영역-선택과-제외-영역), [표시 방식](features/번역모드.md#표시-방식) |
| OCR 엔진 | [OCR엔진.md](features/OCR엔진.md) | [Windows OCR](features/OCR엔진.md#windows-ocr), [Windows 언어 설치](features/OCR엔진.md#windows-언어-설치-방법), [PaddleOCR](features/OCR엔진.md#paddleocropenvino), [엔진 선택 기준](features/OCR엔진.md#엔진-선택-기준) |
| 번역 서비스 | [번역서비스.md](features/번역서비스.md) | [DeepL API](features/번역서비스.md#deepl-api-추천), [사용량 확인](features/번역서비스.md#deepl-사용량-확인), [Google 비공식 API](features/번역서비스.md#google-번역-비공식-api), [Google Apps Script](features/번역서비스.md#google-apps-script) |
| 유저 사전 | [유저사전.md](features/유저사전.md) | [직접 추가](features/유저사전.md#직접-추가), [화면 OCR로 채우기](features/유저사전.md#화면-ocr로-채우기), [저장 위치](features/유저사전.md#저장-위치), [운영 팁](features/유저사전.md#운영-팁) |

## 빠른 선택 기준

- 채팅만 번역하려면 [채팅 번역](features/번역모드.md#채팅-번역)을 먼저 설정하세요.
- 화면 UI와 안내 문구까지 번역하려면 [전체화면 번역](features/번역모드.md#전체화면-번역)을 사용하세요.
- Windows OCR에서 중국어/일본어가 실패하면 [Windows 언어 설치 방법](features/OCR엔진.md#windows-언어-설치-방법)을 확인하세요.
- 인식 품질이 더 중요하고 속도 저하를 감수할 수 있으면 [PaddleOCR](features/OCR엔진.md#paddleocropenvino)을 사용하세요.
- 번역 서비스는 가능하면 [DeepL API](features/번역서비스.md#deepl-api-추천)를 권장합니다.
- 반복 표현이나 게임 용어는 [유저 사전](features/유저사전.md)에 등록해 API 호출과 오역을 줄이세요.

## 관련 문서

- [빌드 가이드](BUILD.md)
- [기본 유저 사전 원본](user_dictionary.csv)
