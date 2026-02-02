FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/Medipiel.Api/Medipiel.Api.csproj ./Medipiel.Api/
COPY src/Medipiel.Competitors.Abstractions/Medipiel.Competitors.Abstractions.csproj ./Medipiel.Competitors.Abstractions/
RUN dotnet restore ./Medipiel.Api/Medipiel.Api.csproj

COPY src/Medipiel.Api/ ./Medipiel.Api/
COPY src/Medipiel.Competitors.Abstractions/ ./Medipiel.Competitors.Abstractions/
WORKDIR /src/Medipiel.Api
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 5000
ENV ASPNETCORE_URLS=http://0.0.0.0:5000
ENTRYPOINT ["dotnet", "Medipiel.Api.dll"]
