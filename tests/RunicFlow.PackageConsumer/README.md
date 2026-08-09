# Flow packed-package consumer

This executable compiles and runs the public headless process, checkpoint, and operation scenarios without using internal APIs.
The default build uses a `ProjectReference` for repository development. Setting
`RunicFlowPackageVersion` switches the same source to a `PackageReference`, so this is
also an executable packed-package gate:

```console
dotnet pack -c Release -p:PackageVersion=0.0.0-local -o tests/RunicFlow.PackageConsumer/obj/packages src/RunicFlow
dotnet restore -p:RunicFlowPackageVersion=0.0.0-local -p:RestoreAdditionalProjectSources=obj/packages tests/RunicFlow.PackageConsumer
dotnet run -c Release --no-restore -p:RunicFlowPackageVersion=0.0.0-local --project tests/RunicFlow.PackageConsumer
```

The executable exits zero and prints one `PASS` line only when all public
scenarios behave as expected.
