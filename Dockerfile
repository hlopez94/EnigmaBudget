FROM node:24-alpine AS web-build
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0.402 AS publish
WORKDIR /source
COPY src/EnigmaBudget.Api/EnigmaBudget.Api.csproj src/EnigmaBudget.Api/
RUN dotnet restore src/EnigmaBudget.Api/EnigmaBudget.Api.csproj
COPY src/EnigmaBudget.Api/ src/EnigmaBudget.Api/
COPY --from=web-build /web/dist/web/browser/ src/EnigmaBudget.Api/wwwroot/
RUN dotnet publish src/EnigmaBudget.Api/EnigmaBudget.Api.csproj --configuration Release --no-restore --output /publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=publish /publish/ ./
USER $APP_UID
ENTRYPOINT ["dotnet", "EnigmaBudget.Api.dll"]
