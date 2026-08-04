# Flow Native-AOT smoke executable

This project exercises Flow's public contracts without reflection, dynamic code,
serializer discovery, suppressions, or trim descriptors.

Restore the intended RID before publishing the NativeAOT smoke executable:

```console
dotnet restore tests/RunicFlow.AotSmoke
dotnet pack -c Release -p:PackageVersion=0.0.0-local -o tests/RunicFlow.PackageConsumer/obj/packages src/RunicFlow
dotnet restore -r win-x64 -p:RunicFlowPackageVersion=0.0.0-local -p:RestoreAdditionalProjectSources=../RunicFlow.PackageConsumer/obj/packages -p:PublishAot=true -p:PublishTrimmed=true tests/RunicFlow.AotSmoke
dotnet publish -c Release -r win-x64 --no-restore -p:RunicFlowPackageVersion=0.0.0-local -p:PublishAot=true -p:PublishTrimmed=true -o tests/RunicFlow.AotSmoke/obj/aot-publish tests/RunicFlow.AotSmoke
tests/RunicFlow.AotSmoke/obj/aot-publish/RunicFlow.AotSmoke.exe
```

`RunicFlowPackageVersion` switches from the development project reference to the packed
package. Run the executable in the publish directory. Success prints one stable
`PASS` line and exits zero. Validate additional runtime identifiers with their own
RID-specific restore before treating them as release evidence.
