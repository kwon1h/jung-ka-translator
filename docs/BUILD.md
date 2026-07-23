# Build and Release Guide

이 프로젝트의 배포 기준 산출물은 `artifacts/release/GameOverlayTranslator.exe` 단일 파일입니다.

## 로컬 개발 빌드

저장소에 포함된 로컬 .NET SDK를 우선 사용합니다.

```powershell
.\.dotnet\dotnet.exe build src\GameOverlayTranslator.App\GameOverlayTranslator.App.csproj
```

로컬 `.dotnet\dotnet.exe`가 없는 환경에서는 설치된 `dotnet`으로 같은 명령을 실행할 수 있습니다.

```powershell
dotnet build src\GameOverlayTranslator.App\GameOverlayTranslator.App.csproj
```

## 회귀 테스트

테스트는 일반 `dotnet test`가 아니라 회귀 하네스 프로젝트를 실행하는 방식입니다.

```powershell
.\.dotnet\dotnet.exe run --project tests\GameOverlayTranslator.RegressionTests\GameOverlayTranslator.RegressionTests.csproj
```

CI에서는 Release 구성으로 같은 하네스를 실행합니다.

```powershell
dotnet run --project tests\GameOverlayTranslator.RegressionTests\GameOverlayTranslator.RegressionTests.csproj --configuration Release
```

## 로컬 배포 빌드

```powershell
.\scripts\build-release.ps1
```

이 스크립트는 다음 작업을 수행합니다.

- 기존 `src/**/bin`, `src/**/obj`, `tests/**/bin`, `tests/**/obj`, `artifacts/GameOverlayTranslator-win-x64`, `artifacts/release`를 정리합니다.
- Windows x64 self-contained single-file exe를 Release 구성으로 publish합니다.
- publish 과정에서 생긴 보조 파일을 제거하고 `artifacts/release/GameOverlayTranslator.exe`만 남깁니다.
- 최종 release 폴더에 exe 하나만 있는지 검증합니다.

성공 조건:

```text
artifacts/release/GameOverlayTranslator.exe
```

위 파일 하나만 release 폴더에 있어야 합니다.

## GitHub 배포

GitHub 배포는 `v*` 태그 push로 실행됩니다. 워크플로는 `.github/workflows/release.yml`에 정의되어 있습니다.

1. `main`이 깨끗하고 원격과 동기화되어 있는지 확인합니다.

```powershell
git status --short --branch
git pull --ff-only origin main
```

2. 로컬에서 빌드, 테스트, release 빌드를 확인합니다.

```powershell
.\.dotnet\dotnet.exe build src\GameOverlayTranslator.App\GameOverlayTranslator.App.csproj
.\.dotnet\dotnet.exe run --project tests\GameOverlayTranslator.RegressionTests\GameOverlayTranslator.RegressionTests.csproj
.\scripts\build-release.ps1
```

3. 변경사항을 커밋하고 `main`에 push합니다.

```powershell
git add <changed-files>
git commit -m "<message>"
git push origin main
```

4. 최신 태그를 확인하고 새 버전 태그를 만듭니다.

```powershell
git tag --list "v*" --sort=-v:refname
git tag -a vX.Y.Z -m "Release vX.Y.Z"
git push origin vX.Y.Z
```

5. GitHub Actions의 `Build and Release` 실행이 성공했는지 확인합니다.

```powershell
$uri = "https://api.github.com/repos/kwon1h/jung-ka-translator/actions/runs?event=push&per_page=10"
(Invoke-RestMethod -Uri $uri -Headers @{ "User-Agent" = "local" }).workflow_runs |
    Select-Object -First 5 id, name, head_branch, status, conclusion, html_url
```

6. Release와 exe asset이 생성되었는지 확인합니다.

```powershell
$tag = "vX.Y.Z"
$release = Invoke-RestMethod -Uri "https://api.github.com/repos/kwon1h/jung-ka-translator/releases/tags/$tag" -Headers @{ "User-Agent" = "local" }
$release.html_url
$release.assets | Select-Object name, size, browser_download_url
```

## 정리

```powershell
.\scripts\clean.ps1
```

삭제 대상은 생성 산출물인 `src/**/bin`, `src/**/obj`, `tests/**/bin`, `tests/**/obj`, `artifacts/GameOverlayTranslator-win-x64`, `artifacts/release`입니다.

다음 경로는 삭제하지 않습니다.

- `.dotnet`
- `.nuget`
- `.dotnet-home`
- `.appdata`
- `docs/user_dictionary.csv`
- `font`

## 기본 사전

기본 사전 원본은 `docs/user_dictionary.csv`입니다.

앱 프로젝트는 이 CSV를 embedded resource로 포함합니다.

```xml
<EmbeddedResource Include="..\..\docs\user_dictionary.csv"
                  Link="Assets\user_dictionary.csv"
                  LogicalName="GameOverlayTranslator.App.Assets.user_dictionary.csv" />
```

배포 폴더에는 CSV 파일을 따로 복사하지 않습니다. 실행 중 사용자 사전은 `%LOCALAPPDATA%\GameOverlayTranslator\user_dictionary.csv`에 저장되며, 기본 사전과 별개로 유지됩니다.
