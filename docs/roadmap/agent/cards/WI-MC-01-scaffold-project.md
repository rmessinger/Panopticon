# Task Card `WI-MC-01`: Scaffold the ModCollector project

| Field | Value |
|---|---|
| Card ID | `WI-MC-01` |
| Plan | [ModCollector Tool Plan](../../plans/mod-collector.md) |
| Status | `Ready` |
| Type | `mechanical-refactor` |
| Effort | `S` |
| Prerequisites | none |
| Diagrams | N/A: standalone tool, see plan |

## Goal

Create `src/OpenSage.Tools.ModCollector/` as a buildable project skeleton that compiles
and is wired into the solution, matching the BigTool pattern exactly.

## Steps

1. Create `src/OpenSage.Tools.ModCollector/OpenSage.Tools.ModCollector.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="CommandLineParser" />
    <PackageReference Include="ConsoleTables" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\OpenSage.FileFormats.Big\OpenSage.FileFormats.Big.csproj" />
  </ItemGroup>

</Project>
```

Do NOT add `<TargetFramework>`: it is inherited from `src/Directory.Build.props` (`net10.0`).

2. Add the project to `src/OpenSage.sln`. Use `dotnet sln src/OpenSage.sln add src/OpenSage.Tools.ModCollector/OpenSage.Tools.ModCollector.csproj`.

3. Create `src/OpenSage.Tools.ModCollector/Program.cs` with a minimal skeleton:

```csharp
using System;
using CommandLine;

namespace OpenSage.Tools.ModCollector;

class Program
{
    static int Main(string[] args)
    {
        Console.WriteLine("ModCollector: not yet implemented");
        return 0;
    }
}
```

4. Run `dotnet build src/OpenSage.Tools.ModCollector` and confirm zero errors.

## Acceptance criteria

- `dotnet build src/OpenSage.Tools.ModCollector` exits 0.
- Project appears in `src/OpenSage.sln`.
- No new build warnings introduced.

## Validation

```sh
dotnet build src/OpenSage.Tools.ModCollector
dotnet run --project src/OpenSage.Tools.ModCollector
```

## Result

<!-- append after completing -->
