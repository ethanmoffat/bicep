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

A `.biceptest` file is not a deployable template. Referencing one from a `module` declaration, or
using one as the target of a test, is an error:

```console
$ bicep build main.bicep
main.bicep(1,10) : Error BCP461: A ".biceptest" file declares tests and is not a deployable template, so it cannot be referenced here. Reference the Bicep file under test instead.
```

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

- **Discovery** is how the CLI finds test files. You either name a test file on the command line or match several with `--pattern`.
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

### Running many test files with `--pattern`

`--pattern` runs every test file matching a glob, relative to the current directory:

```console
bicep test --pattern "**/*.biceptest"
```

Files are processed in a stable, sorted order, and a single summary covers the whole run. When more
than one file is involved each result names its test file, because test names are only unique within
a file.

A pattern that matches nothing is an error. An empty run is never reported as a run that passed.

`--pattern` and a named input file are alternatives; supply one or the other.

### Listing what would run with `--list`

`--list` reports the inventory of a test file — which tests it declares and which targets each one
resolves to — without restoring, compiling or evaluating any target:

```console
bicep test naming.biceptest --list
```

This answers "what would run". It deliberately says nothing about whether those targets compile or
pass. `--list` combines with `--pattern` to inventory a whole suite.

A test that resolves to no targets is reported on stderr and makes `--list` exit non-zero, so a
selector that has silently stopped matching cannot hide behind an empty list.

### Machine-readable output with `--output-format json`

`--output-format json` writes a versioned document to stdout and keeps every piece of progress text,
diagnostic and warning on stderr. A host process can therefore parse stdout whether the run
succeeded or failed:

```console
$ bicep test storage-failing.biceptest --output-format json
{
  "version": "1.0",
  "mode": "run",
  "cases": [
    {
      "caseId": "storage-failing.biceptest#prefixTooLong#storage.bicep",
      "testFile": "storage-failing.biceptest",
      "testName": "prefixTooLong",
      "target": "storage.bicep",
      "status": "failed",
      "error": null,
      "assertions": {
        "total": 2,
        "failed": 1,
        "failedNames": [
          "nameWithinLengthLimit"
        ]
      }
    }
  ],
  "summary": {
    "total": 1,
    "passed": 0,
    "failed": 1,
    "skipped": 0
  }
}
```

The same option applies to `--list`, where `mode` is `list` and each case has a status of `listed`
or `unresolved`:

```console
$ bicep test naming.biceptest --list --output-format json
{
  "version": "1.0",
  "mode": "list",
  "cases": [
    {
      "caseId": "naming.biceptest#namingPolicy#modules/blobStorage.bicep",
      "testFile": "naming.biceptest",
      "testName": "namingPolicy",
      "target": "modules/blobStorage.bicep",
      "status": "listed",
      "error": null
    },
    {
      "caseId": "naming.biceptest#namingPolicy#modules/fileStorage.bicep",
      "testFile": "naming.biceptest",
      "testName": "namingPolicy",
      "target": "modules/fileStorage.bicep",
      "status": "listed",
      "error": null
    }
  ],
  "summary": {
    "total": 2,
    "listed": 2,
    "unresolved": 0
  }
}
```

Notes on the contract:

- `version` changes only when the shape changes in a way a consumer must react to. New optional
  properties may be added without a version bump.
- `caseId` is built from test-file-relative information only, so the same case has the same identity
  regardless of the directory the CLI was invoked from.
- `target` is `null` when a test could not be resolved to any target; there is no target to name.
- `assertions` is present only for cases that were actually evaluated. A skipped case never reached
  its assertions, and reporting zero counts would be indistinguishable from a target that declares
  none.
- The document carries identities and outcomes only. Parameter values, template content and other
  payloads are never included.

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

Listing the same test file reports the targets without evaluating them:

```console
$ bicep test naming.biceptest --list
naming.biceptest: namingPolicy -> modules/blobStorage.bicep
naming.biceptest: namingPolicy -> modules/fileStorage.bicep
```

Running the whole example folder with a pattern gathers every test file into one run:

```console
$ bicep test --pattern "*.biceptest"
[✓] Evaluation naming.biceptest: namingPolicy (modules/blobStorage.bicep) Passed!
[✓] Evaluation naming.biceptest: namingPolicy (modules/fileStorage.bicep) Passed!
[✗] Evaluation storage-failing.biceptest: prefixTooLong (storage.bicep) Failed at 1 / 2 assertions!
	[✗] Assertion nameWithinLengthLimit failed!
[✓] Evaluation storage.biceptest: validPrefix (storage.bicep) Passed!
[✓] Evaluation storage.biceptest: prefixAtLengthLimit (storage.bicep) Passed!
Evaluation Summary: Failure!
Total: 5 - Success: 4 - Skipped: 0 - Failed: 1
```

The command exits with code `1` because `storage-failing.biceptest` is expected to fail. The failure
of one file does not stop the others from running, and one summary reports the aggregate.

Supplying a file that is neither `.bicep` nor `.biceptest` is rejected:

```console
$ bicep test bicepconfig.json
The specified input "...\bicepconfig.json" was not recognized as a Bicep or Bicep test file. Valid files must use either the .bicep or .biceptest extension.
```

## Current limitations

- Assertions must be written in the Bicep file under test; they cannot yet be authored in the test file itself.
- Parameter values are written inline in the test declaration, and the same values apply to every target a test selects.
- Tests evaluate templates offline. They do not deploy resources, call Azure, or validate authorization.

For background and ongoing discussion, see [Bicep Experimental Test Framework](https://github.com/Azure/bicep/issues/11967).
