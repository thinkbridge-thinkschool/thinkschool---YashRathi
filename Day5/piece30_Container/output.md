# Piece 30 — Container Image from `dotnet publish` (No Dockerfile)

## csproj Container Properties

```xml
<ContainerRepository>quotes-api</ContainerRepository>
<ContainerImageTag>0.1.0</ContainerImageTag>
<ContainerBaseImage>mcr.microsoft.com/dotnet/aspnet:10.0-alpine</ContainerBaseImage>
```

> **Note:** In .NET 10 SDK, `ContainerImageName` is obsolete — use `ContainerRepository`.  
> Alpine uses musl libc, so publish must target the musl RID: `--os linux-musl --arch x64`.

## Publish Command

```
dotnet publish --os linux-musl --arch x64 -p:PublishProfile=DefaultContainer
```

### Output (abbreviated)

```
Building image 'quotes-api' with tags '0.1.0' on top of base image 'mcr.microsoft.com/dotnet/aspnet:10.0-alpine'.
Pushed image 'quotes-api:0.1.0' to local registry via 'docker'.
```

## Image Size

```
IMAGE              ID             DISK USAGE   CONTENT SIZE
quotes-api:0.1.0   edbc7a76f11d        196MB         59.5MB
```

## `docker run` Output

```
docker run -d --name quotes-api-test -p 8080:8080 \
  -e Jwt__Key="supersecretkey1234567890123456789012345" \
  -e ASPNETCORE_URLS="http://+:8080" \
  -e ConnectionStrings__DefaultConnection="Data Source=/tmp/quotes.db" \
  quotes-api:0.1.0

Container logs:
[WRN] Storing keys in a directory '/home/app/.aspnet/DataProtection-Keys' ...
[WRN] No XML encryptor configured ...
[WRN] Overriding HTTP_PORTS '8080'. Binding to URLS 'http://+:8080'
[INF] HTTP GET /health responded 200 in 172.3835 ms
```

## curl `/health`

```
$ curl -s -w "\nHTTP Status: %{http_code}\n" http://localhost:8080/health
Healthy
HTTP Status: 200
```

## Key Learnings

| Topic | Detail |
|---|---|
| No Dockerfile needed | `dotnet publish /t:PublishContainer` (or `-p:PublishProfile=DefaultContainer`) builds the image via MSBuild |
| Alpine + SQLite | Alpine uses musl libc; must use `--os linux-musl` RID so `libe_sqlite3.so` is the musl variant |
| Writable path | Default connection string `Data Source=quotes.db` hits `/app` which is read-only; override to `/tmp/quotes.db` in the container |
| Config injection | `__` double-underscore maps to `:` in .NET config — e.g. `-e Jwt__Key=...` becomes `Jwt:Key` |
| .NET 10 rename | `ContainerImageName` → `ContainerRepository` (CONTAINER003 warning) |
