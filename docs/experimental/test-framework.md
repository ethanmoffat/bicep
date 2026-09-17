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

## Selecting targets with `match`

A test can also select its targets from the filesystem instead of naming one. Omit the literal path and declare a `match` selector in the test body:

```bicep
test namingPolicy = {
  match: {
    root: 'modules'
    include: ['*.bicep']
    exclude: ['_*.bicep']
  }
  params: {
    namePrefix: 'contoso'
    location: 'eastus'
  }
}
```

The test then produces one result per matched file. This is how you apply a single policy — a naming rule, a required-tag rule — to a whole folder without listing every file.

| Property | Required | Default | Meaning |
|----------|----------|---------|---------|
| `root` | No | `.` | The directory the patterns are relative to, itself relative to the directory containing the test file |
| `include` | Yes | — | Glob patterns selecting files under the root |
| `exclude` | No | `[]` | Glob patterns removing files from the included set |
| `allowEmpty` | No | `false` | Whether matching no files at all is acceptable |

Rules:

- Every value must be a compile-time constant. A selector that cannot be read from source alone is rejected, so the set of files a test covers is always visible in the test file.
- `include` and `exclude` patterns may not be rooted and may not contain `..` segments. Only `root` can widen the search, so the bounds of the walk are always a single, obvious value.
- Excludes always win over includes.
- Symbolic links and directory junctions under the root are not followed.
- Matching zero files is an error unless `allowEmpty` is `true`. A selector that has quietly stopped matching anything should not pass vacuously.
- A test uses either a literal target path or a `match` selector, never both.

### Discovery versus selection

These are two different things, and they are deliberately kept apart:

- **Discovery** is how the CLI finds test files. Today you name the test file on the command line.
- **Selection** is how a test finds the files it applies to. That is what `match` does, and it is owned by the test file rather than by whoever invokes the CLI.

Because selection lives in the test file, running the same test from a different working directory — or from CI — always covers the same set of files.

### Each target is bound independently

Every matched target is compiled and bound on its own. The parameters in `params` are validated against that target's own parameters, not against a merged view of all targets. A target that fails to compile, or that needs a parameter the test does not supply, is reported against that target alone; the remaining targets still run, and the overall command still exits non-zero.

## Editor support

`.biceptest` files are registered as their own language (`bicep-test`) in the Bicep VS Code extension and are handled by the Bicep language server. Opening a `.biceptest` file gives syntax highlighting, diagnostics, formatting and completions.

Top-level completions in a `.biceptest` file are scoped to what a test file can declare:

| Offered | Not offered |
|---------|-------------|
| `test` (only when `testFramework` is enabled), `metadata`, `param`, `var`, `type`, `func`, `import` | `resource`, `module`, `output`, `targetScope`, `extension` |

Deployment-only declarations are omitted because a test file is never deployed. The `test` keyword is hidden unless the `testFramework` experimental feature is enabled, so the completion list matches what will actually compile.

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
| `modules/blobStorage.bicep`, `modules/fileStorage.bicep` | Two modules sharing a naming policy |
| `modules/_naming.bicep` | A helper that is not a deployable module, excluded by the selector |
| `naming.biceptest` | One test applied to every module via `match` |

Running the passing tests:

```console
$ bicep test storage.biceptest
WARNING: The following experimental Bicep features have been enabled: TestFramework. Experimental features should be enabled for testing purposes only, as there are no guarantees about the quality or stability of these features. Do not enable these settings for any production usage, or your production environment may be subject to breaking.
[✓] Evaluation validPrefix (storage.bicep) Passed!
[✓] Evaluation prefixAtLengthLimit (storage.bicep) Passed!
All 2 evaluations passed!
```

The command exits with code `0`. Each result names both the test and the target it was evaluated against, because one test may cover several targets.

Running the failing test shows which assertion failed:

```console
$ bicep test storage-failing.biceptest
[✗] Evaluation prefixTooLong (storage.bicep) Failed at 1 / 2 assertions!
	[✗] Assertion nameWithinLengthLimit failed!
Evaluation Summary: Failure!
Total: 1 - Success: 0 - Skipped: 0 - Failed: 1
```

The command exits with code `1`. In this example `namePrefix` is long enough that the generated storage account name exceeds the 24 character limit asserted by `storage.bicep`.

Running the selector-based test applies one declaration to both modules, while skipping the excluded helper:

```console
$ bicep test naming.biceptest
[✓] Evaluation namingPolicy (modules/blobStorage.bicep) Passed!
[✓] Evaluation namingPolicy (modules/fileStorage.bicep) Passed!
All 2 evaluations passed!
```

Adding another module to `modules/` puts it under the same policy automatically.

Supplying a file that is neither `.bicep` nor `.biceptest` is rejected:

```console
$ bicep test bicepconfig.json
The specified input "...\bicepconfig.json" was not recognized as a Bicep or Bicep test file. Valid files must use either the .bicep or .biceptest extension.
```

## Current limitations

- Assertions must be written in the Bicep file under test; they cannot yet be authored in the test file itself.
- Parameter values are written inline in the test declaration, and the same values apply to every target a test selects.
- Test file discovery is a single path on the command line; there is no glob over test files yet.
- Tests evaluate templates offline. They do not deploy resources, call Azure, or validate authorization.

For background and ongoing discussion, see [Bicep Experimental Test Framework](https://github.com/Azure/bicep/issues/11967).
