<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup><RootNamespace>{{NAMESPACE}}.Host</RootNamespace><AssemblyName>Application.Host</AssemblyName></PropertyGroup>
  <ItemGroup><ProjectReference Include="../../src/Metarill.Core/Metarill.Core.csproj" /><ProjectReference Include="../../src/Metarill.Data/Metarill.Data.csproj" /><ProjectReference Include="../../src/Metarill.Exports/Metarill.Exports.csproj" /><ProjectReference Include="../../src/Metarill.Files/Metarill.Files.csproj" /></ItemGroup>
  <ItemGroup><Compile Include="../generated/**/*.cs" /><Content Include="../metadata/project.json" Link="metadata/project.json" CopyToOutputDirectory="PreserveNewest" /><Content Include="../metadata/files.json" Link="metadata/files.json" CopyToOutputDirectory="PreserveNewest" /></ItemGroup>
</Project>
