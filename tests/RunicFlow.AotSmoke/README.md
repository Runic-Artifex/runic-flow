# Flow NativeAOT smoke executable

This project exercises the headless process, checkpoint, operation, and
Application Bridge integration surfaces without reflection discovery, dynamic
code, suppressions, or trim descriptors.

Repository verification publishes it from project references. The isolated
package gate first runs `eng/pack.sh`, then invokes:

```console
bash tests/RunicFlow.ApplicationBridge.PackageConsumer/Test-PackageConsumer.sh \
  0.1.0-preview.local.1 /path/to/packages linux-x64
```

`RunicFlowPackageVersion` switches the project from development references to
the packed `RunicFlow` and `RunicFlow.ApplicationBridge` packages. Success prints
one stable `PASS` line and exits zero.
