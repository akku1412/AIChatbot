# ChatGPTClone.Api on Linux (Render, or any Docker host). Build context: this folder.
#   docker build -t aichat-api .
#   docker run -p 8080:8080 --env-file <your secrets file> aichat-api
# Secrets are never baked in: set them as environment variables on the host (see README, "Free deployment").

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ChatGPTClone.Api.csproj ./
RUN dotnet restore ChatGPTClone.Api.csproj
COPY . ./
# OCR language data (git-ignored locally; the same files scripts/download-models.ps1 fetches).
RUN mkdir -p tessdata \
 && curl -fsSL -o tessdata/eng.traineddata https://github.com/tesseract-ocr/tessdata_fast/raw/main/eng.traineddata \
 && curl -fsSL -o tessdata/hin.traineddata https://github.com/tesseract-ocr/tessdata_fast/raw/main/hin.traineddata
RUN dotnet publish ChatGPTClone.Api.csproj -c Release -o /app --no-restore \
 && rm -f /app/appsettings.Development.json

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
# Tesseract OCR (the NuGet package ships Windows binaries only) and fonts for PDF rendering.
RUN apt-get update \
 && apt-get install -y --no-install-recommends libtesseract5 liblept5 libfontconfig1 \
 && rm -rf /var/lib/apt/lists/* \
 && ln -s /usr/lib/x86_64-linux-gnu/libdl.so.2 /usr/lib/x86_64-linux-gnu/libdl.so
WORKDIR /app
COPY --from=build /app ./
# The names the Tesseract wrapper looks for, next to the app.
RUN mkdir -p x64 \
 && ln -s /usr/lib/x86_64-linux-gnu/liblept.so.5 x64/libleptonica-1.82.0.so \
 && ln -s /usr/lib/x86_64-linux-gnu/libtesseract.so.5 x64/libtesseract50.so \
 && mkdir -p App_Data/uploads && chown -R app:app App_Data

ENV ASPNETCORE_ENVIRONMENT=Production \
    ForwardedHeaders__Enabled=true \
    DOTNET_gcServer=0
USER app
# Render sets PORT; elsewhere 8080.
ENTRYPOINT ["sh", "-c", "ASPNETCORE_HTTP_PORTS=${PORT:-8080} exec dotnet ChatGPTClone.Api.dll"]
