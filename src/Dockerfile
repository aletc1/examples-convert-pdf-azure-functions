FROM mcr.microsoft.com/azure-functions/dotnet:2.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/core/sdk:2.1-stretch AS build
WORKDIR /src
COPY ["PdfConverterFunction.csproj", ""]
RUN dotnet restore "./PdfConverterFunction.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "PdfConverterFunction.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "PdfConverterFunction.csproj" -c Release -o /home/site/wwwroot

FROM base AS final
WORKDIR /home/site/wwwroot
COPY --from=publish ["/home/site/wwwroot", "/home/site/wwwroot"]
ENV AzureWebJobsScriptRoot=/home/site/wwwroot
RUN mkdir -p /usr/share/man/man1
RUN apt-get update && apt-get upgrade -y
RUN apt-get -y install default-jre-headless libreoffice