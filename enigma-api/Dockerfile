#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["EnigmaBudget.WebApi/EnigmaBudget.WebApi.csproj", "EnigmaBudget.WebApi/"]
RUN dotnet restore "EnigmaBudget.WebApi/EnigmaBudget.WebApi.csproj"
COPY . .
WORKDIR "/src/EnigmaBudget.WebApi"
RUN dotnet build "EnigmaBudget.WebApi.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "EnigmaBudget.WebApi.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "EnigmaBudget.WebApi.dll"]