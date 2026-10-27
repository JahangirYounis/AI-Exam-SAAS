# Website module

Open `AiExamProctoring.sln` in Visual Studio, or run from this directory:

```powershell
dotnet build .\AiExamProctoring.sln
dotnet run --project .\AiExamProctoring\AiExamProctoring.csproj
```

ASP.NET Core 8 owns the UI, authentication, exams, SQL database, reports and evidence.
Its `Models/` folder contains C# domain entities, not AI weights.
The Python services and training assets are in the sibling `AI/` directory.

AI connections are configured by `FaceIdentity:BaseUrl`, `BehaviorAnalysis:BaseUrl`,
`ObjectDetection:BaseUrl` and `SpeechRecognition:BaseUrl` in
`AiExamProctoring/appsettings.json` (ports 8001-8004).
Start AI with `..\START_AI_SERVICES.ps1` before using identity/precheck flows.
The website can be built independently of Python and model files.

The SQL connection and existing private `App_Data` files were preserved.
See the [project guide](../README.md) for setup and training instructions.
