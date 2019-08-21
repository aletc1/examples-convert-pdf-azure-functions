# How to convert an office document to PDF using Azure Functions v2 and Docker
There are a lot of good paid conversion libraries for .NET, but in this example we will see how to do this conversion for *free* (bear with me, there is nothing free in this world 😉 but at least you will pay only for hosting) using Azure Functions v2, Docker and LibreOffice.

## LibreOffice to the rescue

[LibreOffice]([https://libreoffice.org](https://libreoffice.org/)) is a free, open source office suite, capable of manipulating Microsoft Office document formats. It has a command line functionality that converts any office document (Word, PowerPoint...) into PDF.

On Linux, the command line is:

```
/usr/bin/libreoffice --norestore --nofirststartwizard --headless --convert-to pdf source-file.docx
```

On Windows:

```
C:\Program Files\LibreOffice\program\soffice.exe -norestore -nofirststartwizard -headless -convert-to pdf source-file.docx
```

This will create a .PDF document in the current location with the same name of the source file.

So, ¿Why not try to wrap LibreOffice command line and put it as a service?

> As a side note, this is similar as using the not supported [Server Automation in Microsoft Office when using Windows](https://support.microsoft.com/en-us/help/257757/considerations-for-server-side-automation-of-office), but with the difference that this is more lightweight and works with Linux. In any case, take into consideration that each conversion HTTP would open a new LibreOffice process so, you will have a limited number of parallel conversions depending on your CPU and RAM.

## Creating the conversion service as an Azure Function with an HttpTrigger

The idea is to create an Azure function that do the following:

1. Receives a URL and downloads the original document (Word, PowerPoint, etc)
2. Calls LibreOffice and executes the conversion
3. Store the converted PDF into a Azure Blob Storage
4. Returns a secure URL for downloading the converted PDF

You can check the full function source code [here](https://github.com/aletc1/examples-convert-pdf-azure-functions/blob/master/src/PdfConverterFunction.cs). However, the most important part is this:

```csharp
// Convert file to PDF using libreoffice
var pdfProcess = new Process();
pdfProcess.StartInfo.FileName = LIBRE_OFFICE_BIN;
pdfProcess.StartInfo.Arguments = $"-norestore -nofirststartwizard -headless -convert-to pdf \"{sourceFileName}\"";
pdfProcess.StartInfo.WorkingDirectory = Path.GetDirectoryName(sourceFileName); //This is really important
pdfProcess.Start();

// Wait while document is converting
while (pdfProcess.IsRunning())
{
	await Task.Delay(500);
}

// Check if file was converted properly
var destinationFileName = $"{Path.Combine(Path.GetDirectoryName(sourceFileName), Path.GetFileNameWithoutExtension(sourceFileName))}.pdf";
if (!File.Exists(destinationFileName))
{
	return new BadRequestObjectResult("Error converting file to PDF");
}
```

Even, we can use the same function for Windows and Linux (Debugging purposes) using conditional compiling (Supposing that you Debug on Windows and deploy on Linux):

```csharp
#if DEBUG
        const string LIBRE_OFFICE_BIN = @"C:\Program Files\LibreOffice\program\soffice.exe";
#else        
        const string LIBRE_OFFICE_BIN = "/usr/bin/libreoffice";
#endif
```

So far so good... but, ¿How we install LibreOffice in Azure Serverless?

> Remember that in Serverless mode you have absolutely no control of the server programs. Your function may execute "somewhere" in Azure, even in different servers each time.

## Docker to the rescue

Luckly, [Azure Functions v2 runs on Linux](https://docs.microsoft.com/en-us/azure/azure-functions/functions-create-function-linux-custom-image) and we can create a docker container with all the tools we need ❤️. Azure Functions v2 Linux runtime is based on [Debian stretch](https://hub.docker.com/_/microsoft-azure-functions-base) so, we can install LibreOffice without fuss there:

```dockerfile
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
```

To test it locally, do the following:

```bash
docker build . -t pdfconverter
docker run -p 80:80 -e AzureWebJobsStorage="<your-blob-connection-string>" --name pdfconverter pdfconverter
```

This will host the function. You can test the function doing a POST with the following JSON body (make sure that the url is valid):

```
POST http://localhost/api/PdfConverterFunction

{
	"url": "https://server/path/document.docx"
}
```

If all goes well, then you should receive an URL as a response with the converted PDF document.

## Publishing to Azure

Once you have the docker image ready, you can publish it without fuss using the [Web App for Containers](https://azure.microsoft.com/en-us/services/app-service/containers/) service, and following these high level steps:

1. Upload the customized image into [Azure Container Registry](https://azure.microsoft.com/en-us/services/container-registry/) or [Docker Hub](https://hub.docker.com/)
2. Create a [Function App](https://docs.microsoft.com/en-us/azure/azure-functions/functions-create-function-app-portal) and follow the wizard (you will be able to select Linux and directly deploy the container)