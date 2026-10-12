# syntax=docker/dockerfile:1
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
ARG TARGETARCH
WORKDIR /src

COPY src/Lumen.Domain/Lumen.Domain.csproj src/Lumen.Domain/
COPY src/Lumen.Application/Lumen.Application.csproj src/Lumen.Application/
COPY src/Lumen.Infrastructure/Lumen.Infrastructure.csproj src/Lumen.Infrastructure/
COPY src/Lumen.Api/Lumen.Api.csproj src/Lumen.Api/
RUN dotnet restore src/Lumen.Api/Lumen.Api.csproj -a $TARGETARCH

COPY src/ src/
RUN dotnet publish src/Lumen.Api/Lumen.Api.csproj -c Release -a $TARGETARCH --no-restore -o /app \
    && mkdir -p /out/data/documents

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra
WORKDIR /app
COPY --from=build /app .
COPY --from=build --chown=$APP_UID:$APP_UID /out/data /data
EXPOSE 8080
ENTRYPOINT ["./Lumen.Api"]
