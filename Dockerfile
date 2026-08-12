# Build stage: publish the Blazor WebAssembly app. Output is static files
# (HTML/JS/WASM) under publish/wwwroot — there is no server component to run.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish Megruli.App.csproj -c Release -o /app/publish

# Runtime stage: serve those static files with nginx. The fallback to index.html
# is required — Blazor does client-side routing, so any path (e.g. /lesson/xyz)
# needs to still load the app shell rather than 404.
FROM nginx:alpine AS final
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
