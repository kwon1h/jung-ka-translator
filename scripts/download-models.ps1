# PowerShell script to download and extract PaddleOCR models for GameOverlayTranslator
# This script uses curl.exe to bypass .NET HttpClient download limits and handle network issues.

$ModelsDir = Join-Path $env:APPDATA "paddleocr-models"

# Create models root directory if it doesn't exist
if (-not (Test-Path $ModelsDir)) {
    New-Item -ItemType Directory -Path $ModelsDir -Force | Out-Null
}

$Models = @(
    @{
        Name = "ch_PP-OCRv4_det"
        TarName = "ch_PP-OCRv4_det_infer.tar"
        Url = "https://paddleocr.bj.bcebos.com/PP-OCRv4/chinese/ch_PP-OCRv4_det_infer.tar"
    },
    @{
        Name = "ch_ppocr_mobile_v2.0_cls"
        TarName = "ch_ppocr_mobile_v2.0_cls_infer.tar"
        Url = "https://paddleocr.bj.bcebos.com/dygraph_v2.0/ch/ch_ppocr_mobile_v2.0_cls_infer.tar"
    },
    @{
        Name = "ch_PP-OCRv4_rec"
        TarName = "ch_PP-OCRv4_rec_infer.tar"
        Url = "https://paddleocr.bj.bcebos.com/PP-OCRv4/chinese/ch_PP-OCRv4_rec_infer.tar"
    },
    @{
        Name = "japan_PP-OCRv4_rec"
        TarName = "japan_PP-OCRv4_rec_infer.tar"
        Url = "https://paddleocr.bj.bcebos.com/PP-OCRv4/multilingual/japan_PP-OCRv4_rec_infer.tar"
    }
)

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "PaddleOCR Model Downloader for GameOverlayTranslator" -ForegroundColor Cyan
Write-Host "Destination: $ModelsDir" -ForegroundColor Gray
Write-Host "=============================================" -ForegroundColor Cyan

foreach ($Model in $Models) {
    $TargetFolder = Join-Path $ModelsDir $Model.Name
    $ModelFilePath = Join-Path $TargetFolder "inference.pdmodel"
    $ParamFilePath = Join-Path $TargetFolder "inference.pdiparams"

    Write-Host ""
    Write-Host "Checking model: $($Model.Name)..." -ForegroundColor Yellow

    # Check if the model files are already present directly in the folder
    if ((Test-Path $ModelFilePath) -and (Test-Path $ParamFilePath)) {
        Write-Host "Model '$($Model.Name)' already exists and is configured correctly. Skipping." -ForegroundColor Green
        continue
    }

    # Create model target folder
    if (-not (Test-Path $TargetFolder)) {
        New-Item -ItemType Directory -Path $TargetFolder -Force | Out-Null
    }

    $TarFilePath = Join-Path $TargetFolder $Model.TarName

    # Download tar file using curl
    Write-Host "Downloading from: $($Model.Url)" -ForegroundColor Gray
    Write-Host "Downloading $($Model.TarName)... (This may take a moment)" -ForegroundColor Cyan
    
    # Run curl.exe to download with progress indicator
    curl.exe -L -o $TarFilePath $Model.Url

    if (-not (Test-Path $TarFilePath) -or (Get-Item $TarFilePath).Length -eq 0) {
        Write-Error "Failed to download model file for '$($Model.Name)'."
        continue
    }

    Write-Host "Extracting files..." -ForegroundColor Cyan
    # Extract using tar.exe
    tar.exe -xf $TarFilePath -C $TargetFolder

    # Clean up the tar file
    Remove-Item $TarFilePath -Force

    # Move files from nested subdirectories to root folder
    $SubDirs = Get-ChildItem -Path $TargetFolder -Directory
    foreach ($SubDir in $SubDirs) {
        $NestedDir = $SubDir.FullName
        Write-Host "Moving files from nested folder: $($SubDir.Name)" -ForegroundColor Gray
        Get-ChildItem -Path $NestedDir | ForEach-Object {
            Move-Item -Path $_.FullName -Destination $TargetFolder -Force
        }
        Remove-Item -Path $NestedDir -Recurse -Force
    }

    # Final verification
    if ((Test-Path $ModelFilePath) -and (Test-Path $ParamFilePath)) {
        Write-Host "Successfully installed '$($Model.Name)'." -ForegroundColor Green
    } else {
        Write-Warning "Model files were extracted but could not be verified in target path."
    }
}

Write-Host ""
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "All PaddleOCR models are ready to use!" -ForegroundColor Green
Write-Host "=============================================" -ForegroundColor Cyan
