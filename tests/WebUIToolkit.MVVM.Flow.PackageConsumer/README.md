# Flow packed-package consumer

This executable compiles and runs public Flow scenarios without using internal APIs.
The default build uses a `ProjectReference` for repository development. Setting
`FlowPackageVersion` switches the same source to a `PackageReference`, so this is
also an executable packed-package gate:

```console
dotnet pack -c Release -p:PackageVersion=0.0.0-local -o tests/WebUIToolkit.MVVM.Flow.PackageConsumer/obj/packages src/WebUIToolkit.MVVM.Flow
dotnet restore -p:FlowPackageVersion=0.0.0-local -p:RestoreAdditionalProjectSources=obj/packages tests/WebUIToolkit.MVVM.Flow.PackageConsumer
dotnet run -c Release --no-restore -p:FlowPackageVersion=0.0.0-local --project tests/WebUIToolkit.MVVM.Flow.PackageConsumer
```

The executable exits zero and prints one `PASS` line only when all public
scenarios behave as expected.
