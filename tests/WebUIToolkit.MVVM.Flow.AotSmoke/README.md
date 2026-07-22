# Flow Native-AOT smoke executable

This project exercises Flow's public contracts without reflection, dynamic code,
serializer discovery, suppressions, or trim descriptors.

Keep the checked-in `packages.lock.json` portable. The RID-specific AOT restore must
write its lock below ignored `obj/`:

```console
dotnet restore --locked-mode tests/WebUIToolkit.MVVM.Flow.AotSmoke
dotnet pack -c Release -p:PackageVersion=0.0.0-local -o tests/WebUIToolkit.MVVM.Flow.PackageConsumer/obj/packages src/WebUIToolkit.MVVM.Flow
dotnet restore -r win-x64 -p:FlowPackageVersion=0.0.0-local -p:RestoreAdditionalProjectSources=../WebUIToolkit.MVVM.Flow.PackageConsumer/obj/packages -p:PublishAot=true -p:PublishTrimmed=true -p:NuGetLockFilePath=obj/aot.packages.lock.json -p:RestoreLockedMode=false tests/WebUIToolkit.MVVM.Flow.AotSmoke
dotnet publish -c Release -r win-x64 --no-restore -p:FlowPackageVersion=0.0.0-local -p:PublishAot=true -p:PublishTrimmed=true -p:NuGetLockFilePath=obj/aot.packages.lock.json -o tests/WebUIToolkit.MVVM.Flow.AotSmoke/obj/aot-publish tests/WebUIToolkit.MVVM.Flow.AotSmoke
tests/WebUIToolkit.MVVM.Flow.AotSmoke/obj/aot-publish/WebUIToolkit.MVVM.Flow.AotSmoke.exe
```

`FlowPackageVersion` switches from the development project reference to the packed
package. Run the executable in the publish directory. Success prints one stable
`PASS` line and exits zero. Validate additional runtime identifiers with their own
temporary RID-specific restore before treating them as release evidence.
