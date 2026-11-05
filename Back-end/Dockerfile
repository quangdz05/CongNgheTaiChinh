FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY Back-end ./Back-end
RUN dotnet publish Back-end/src/Services/FraudDetectionService/FraudDetectionService.Api/FraudDetectionService.Api.csproj -c Release -o /out
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out ./
RUN mkdir -p /app/data && chown -R app:app /app
ENV ASPNETCORE_URLS=http://+:8080
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "FraudDetectionService.Api.dll"]
