# Android toolchain and verification

## Required tools

- .NET SDK 10.0.401 (`global.json`, exact version).
- .NET MAUI Android workload for that SDK. Install with `dotnet workload install maui-android --skip-manifest-update` from the repository directory. This retains the SDK's supplied workload manifests; inspect `dotnet workload list` to diagnose local/CI differences.
- JDK 21. The initial local build used Eclipse Temurin 21.0.11.
- Android SDK platform 36 and build-tools 36.0.0, plus platform-tools.
- NuGet packages pin MAUI Controls and Blazor WebView to 10.0.20. Shared Blazor packages are 10.0.12.

The initial local environment has Android workload 36.1.69 and MAUI SDK 10.0.20 supplied by Visual Studio. CI installs the Android-only MAUI workload on Ubuntu 24.04 and prints its actual workload/runtime versions in the job log.

## Compile

From the repository root:

```sh
dotnet build src/RecallDeck.Mobile -c Debug -p:AndroidBuildApplicationPackage=false
```

If automatic SDK detection fails, supply the installed directories (replace the example paths):

```sh
dotnet build src/RecallDeck.Mobile -c Debug -p:AndroidBuildApplicationPackage=false -p:AndroidSdkDirectory="/path/to/android-sdk" -p:JavaSdkDirectory="/path/to/jdk-21"
```

On a Windows Visual Studio installation, the Android SDK is commonly under `C:\Program Files (x86)\Android\android-sdk`. Use your actual JDK installation root, not its `bin` directory. Install SDK packages through the supported SDK manager; any license agreement requiring acceptance is a user action. The CI runner uses its provided Android installation and license state.

## Coverage boundaries

The compile command builds Android Debug assemblies without creating an application package or configuring release signing. It does not prove the app launches, renders correctly on Android WebView, handles back navigation or survives suspend/resume. No Android device or emulator was available in the initial local session; do not describe that session as an Android runtime test.

The first shared landing component is covered by an HTML render test and a web browser smoke check. Before a release, use an emulator/device to verify the complete journeys specified in the roadmap. Android lifecycle and recovery issues carry their own runtime acceptance criteria.

## Primary references

- [Microsoft MAUI installation](https://learn.microsoft.com/dotnet/maui/get-started/installation?view=net-maui-10.0)
- [MAUI 10 Android/JDK changes](https://learn.microsoft.com/dotnet/maui/whats-new/dotnet-10?view=net-maui-10.0)
- [Share Razor assets across web and native clients](https://learn.microsoft.com/aspnet/core/blazor/hybrid/class-libraries?view=aspnetcore-10.0)
