# Build Guide

이 프로젝트의 배포 기준 산출물은 `artifacts/release/GameOverlayTranslator.exe` 단일 파일입니다.

## 개발 빌드

```powershell
.\.dotnet\dotnet.exe build src\GameOverlayTranslator.App\GameOverlayTranslator.App.csproj
```

로컬 `.dotnet\dotnet.exe`가 없으면 Windows에 설치된 `dotnet`으로 같은 명령을 실행합니다.

## 테스트

```powershell
.\.dotnet\dotnet.exe run --project tests\GameOverlayTranslator.RegressionTests\GameOverlayTranslator.RegressionTests.csproj
```

## 배포 빌드

```powershell
.\scripts\build-release.ps1
```

이 스크립트는 기존 `bin`, `obj`, 이전 배포 산출물을 정리한 뒤 Windows x64 self-contained single-file exe를 생성합니다. 성공 조건은 `artifacts/release` 안에 `GameOverlayTranslator.exe` 하나만 있는 것입니다.

## 정리

```powershell
.\scripts\clean.ps1
```

삭제 대상은 생성 산출물인 `src/**/bin`, `src/**/obj`, `tests/**/bin`, `tests/**/obj`, `artifacts/GameOverlayTranslator-win-x64`, `artifacts/release`입니다. `.dotnet`, `.nuget`, `.dotnet-home`, `.appdata`, `docs/user_dictionary.csv`, `font` 폴더는 삭제하지 않습니다.

## 기본 사전 관리

기본 사전의 유일한 편집 원본은 `docs/user_dictionary.csv`입니다. 앱 빌드 시 이 CSV는 exe 내부 embedded resource로 포함되며, 배포 폴더에는 CSV 파일이 따로 복사되지 않습니다.

사용자 개인 사전은 실행 중 `%LOCALAPPDATA%\GameOverlayTranslator\user_dictionary.csv`에 저장됩니다. 이 파일은 기본 사전 원본과 별개인 런타임 사용자 데이터입니다.
