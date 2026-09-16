# Bicep Test Framework

> [!WARNING]
> The test framework is an experimental feature. There are no guarantees about its quality or stability, and its syntax and behavior may change in breaking ways. Do not rely on it for production usage.

The Bicep test framework lets you author client-side, offline tests for Bicep files. Tests compile and evaluate the file under test without deploying anything to Azure.

## Enabling the feature

Tests are gated behind the `testFramework` experimental feature. Assertions written inside the Bicep file under test are additionally gated behind the `assertions` feature.

`bicepconfig.json`:

```json
{
  "experimentalFeaturesEnabled": {
    "testFramework": true,
    "assertions": true
  }
}
```

## Test files

Tests may be declared either in an ordinary `.bicep` file or in a dedicated `.biceptest` file. A `.biceptest` file uses the same syntax as a `.bicep` file, but its purpose is to hold tests rather than deployable infrastructure. Keeping tests in `.biceptest` files separates them from the templates they exercise, so test declarations are never emitted as part of a deployment.

A test declaration names the file under test and supplies its parameters:

```bicep
test validPrefix 'storage.bicep' = {
  params: {
    namePrefix: 'contoso'
    location: 'eastus'
  }
}
```

The target path is a literal path relative to the test file.

## Assertions

Assertions are currently authored in the Bicep file under test using the `assert` keyword. Each `assert` is evaluated after the test's parameters are applied.

```bicep
param namePrefix string

var storageAccountName = toLower('${namePrefix}stg')

assert nameIsLowerCase = storageAccountName == toLower(storageAccountName)
assert nameWithinLengthLimit = length(storageAccountName) <= 24
```

## Running tests

```console
bicep test <path-to-test-file>
```

The command accepts either a `.bicep` or a `.biceptest` file.

## Worked example

The complete example lives in [`docs/experimental/examples/test-framework`](./examples/test-framework). It contains:

| File | Purpose |
|------|---------|
| `bicepconfig.json` | Enables the `testFramework` and `assertions` features |
| `storage.bicep` | The template under test, including two `assert` statements |
| `storage.biceptest` | Two passing tests |
| `storage-failing.biceptest` | A test that intentionally violates an assertion |

Running the passing tests:

```console
$ bicep test storage.biceptest
WARNING: The following experimental Bicep features have been enabled: TestFramework. Experimental features should be enabled for testing purposes only, as there are no guarantees about the quality or stability of these features. Do not enable these settings for any production usage, or your production environment may be subject to breaking.
[✓] Evaluation validPrefix Passed!
[✓] Evaluation prefixAtLengthLimit Passed!
All 2 evaluations passed!
```

The command exits with code `0`.

Running the failing test shows which assertion failed:

```console
$ bicep test storage-failing.biceptest
[✗] Evaluation prefixTooLong Failed at 1 / 2 assertions!
	[✗] Assertion nameWithinLengthLimit failed!
Evaluation Summary: Failure!
Total: 1 - Success: 0 - Skipped: 0 - Failed: 1
```

The command exits with code `1`. In this example `namePrefix` is long enough that the generated storage account name exceeds the 24 character limit asserted by `storage.bicep`.

Supplying a file that is neither `.bicep` nor `.biceptest` is rejected:

```console
$ bicep test bicepconfig.json
The specified input "...\bicepconfig.json" was not recognized as a Bicep or Bicep test file. Valid files must use either the .bicep or .biceptest extension.
```

## Current limitations

- Assertions must be written in the Bicep file under test; they cannot yet be authored in the test file itself.
- Each test declaration references a single literal target path.
- Parameter values are written inline in the test declaration.
- Tests evaluate templates offline. They do not deploy resources, call Azure, or validate authorization.

For background and ongoing discussion, see [Bicep Experimental Test Framework](https://github.com/Azure/bicep/issues/11967).
