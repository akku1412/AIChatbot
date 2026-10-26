# Downloads the local AI model files the API needs (run once per machine, and on the server before first start).
#   powershell -ExecutionPolicy Bypass -File .\scripts\download-models.ps1
#
# - multilingual-e5-small (MIT, intfloat): sentence embeddings for semantic document search (int8 ONNX, ~118 MB).
# - Tesseract "fast" language data (Apache-2.0): English + Hindi OCR for scanned PDFs and images (~6 MB).
# Files are skipped when already present. The ONNX model is git-ignored; tessdata is small and committed.

$ErrorActionPreference = 'Stop'
$api = Join-Path $PSScriptRoot '..\ChatGPTClone.Api'

$files = @(
    @{ Url = 'https://huggingface.co/Xenova/multilingual-e5-small/resolve/main/onnx/model_quantized.onnx';
       Path = 'AiModels\multilingual-e5-small\model_quantized.onnx'; MinBytes = 100MB },
    @{ Url = 'https://huggingface.co/intfloat/multilingual-e5-small/resolve/main/sentencepiece.bpe.model';
       Path = 'AiModels\multilingual-e5-small\sentencepiece.bpe.model'; MinBytes = 4MB },
    @{ Url = 'https://github.com/tesseract-ocr/tessdata_fast/raw/main/eng.traineddata';
       Path = 'tessdata\eng.traineddata'; MinBytes = 1MB },
    @{ Url = 'https://github.com/tesseract-ocr/tessdata_fast/raw/main/hin.traineddata';
       Path = 'tessdata\hin.traineddata'; MinBytes = 500KB }
)

foreach ($file in $files) {
    $target = Join-Path $api $file.Path
    if ((Test-Path $target) -and (Get-Item $target).Length -ge $file.MinBytes) {
        Write-Host "OK      $($file.Path)"
        continue
    }
    New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
    Write-Host "GET     $($file.Url)"
    Invoke-WebRequest -Uri $file.Url -OutFile "$target.part" -UseBasicParsing
    if ((Get-Item "$target.part").Length -lt $file.MinBytes) { throw "Download looks incomplete: $($file.Path)" }
    Move-Item -Force "$target.part" $target
    Write-Host "SAVED   $($file.Path)"
}
