# Build stage — Alpine SDK + clang/zlib for Native AOT
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
RUN apk add --no-cache clang zlib-dev
WORKDIR /src

COPY Mcp.Proxy.Server/Mcp.Proxy.Server.csproj Mcp.Proxy.Server/
RUN dotnet restore Mcp.Proxy.Server/Mcp.Proxy.Server.csproj -r linux-musl-x64

COPY Mcp.Proxy.Server/ Mcp.Proxy.Server/
RUN dotnet publish Mcp.Proxy.Server/Mcp.Proxy.Server.csproj \
    -r linux-musl-x64 \
    -c Release \
    -o /app/publish \
    --no-restore

# Runtime stage — plain Alpine; AOT+musl binary only needs musl libc (built-in) + ca-certificates
FROM alpine:3.21
RUN apk add --no-cache ca-certificates
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 5000
ENTRYPOINT ["./Mcp.Proxy.Server"]
