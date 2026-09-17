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

Because selection lives in the test file, running the same test from a different working directory — or from CI — always covers the same set of files, and reports the same case identities.

`match` is also evaluated against the filesystem on every run. Adding a new file that the selector already covers brings it under test on the next run, with no edit to the test file:

```console
$ bicep test main.biceptest
[✓] Evaluation policy (modules/one.bicep) Passed!
All 1 evaluations passed!

$ # add modules/two.bicep, then re-run
$ bicep test main.biceptest
[✓] Evaluation policy (modules/one.bicep) Passed!
[✓] Evaluation policy (modules/two.bicep) Passed!
All 2 evaluations passed!
```

### Each target is bound independently

Every matched target is compiled and bound on its own. The parameters in `params` are validated against that target's own parameters, not against a merged view of all targets. A target that fails to compile, or that needs a parameter the test does not supply, is reported against that target alone; the remaining targets still run, and the overall command still exits non-zero.

## Editor support

`.biceptest` files are registered as their own language (`bicep-test`) in the Bicep VS Code extension and are handled by the Bicep language server. Opening a `.biceptest` file gives syntax highlighting, diagnostics, formatting and completions.

Top-level completions in a `.biceptest` file are scoped to what a test file can declare:

| Offered | Not offered |
|---------|-------------|
| `test` (only when `testFramework` is enabled), `metadata`, `param`, `var`, `type`, `func`, `import` | `resource`, `module`, `output`, `targetScope`, `extension` |

Deployment-only declarations are omitted because a test file is never deployed. The `test` keyword is hidden unless the `testFramework` experimental feature is enabled, so the completion list matches what will actually compile.

`bicep lint` and `bicep format` also accept `.biceptest` files. Both analyse or rewrite the source
only; neither evaluates the tests, and formatting preserves the `.biceptest` extension.

## Assertions

A test can assert about its target in two different ways, and they are deliberately distinct.

### Target-owned assertions

Assertions authored in the Bicep file under test use the `assert` keyword. Each `assert` is evaluated after the test's parameters are applied, so the target must compile *and* be supplied with every parameter it requires.

```bicep
param namePrefix string

var storageAccountName = toLower('${namePrefix}stg')

assert nameIsLowerCase = storageAccountName == toLower(storageAccountName)
assert nameWithinLengthLimit = length(storageAccountName) <= 24
```

### Test-owned assertions

A test may instead bring its own assertions, written in the test file. These ask questions about how
the target is *written* rather than what it evaluates to, so they need no parameter values at all:

```bicep
test moduleSourcePolicy = {
  match: {
    root: 'modules'
    include: ['*.bicep']
  }
  assertions: {
    onlyAllowedResourceTypes: {
      failOn: filter(target.resources, r => r.type != 'Microsoft.Storage/storageAccounts')
      message: 'Modules under modules/ may only declare storage accounts.'
    }
    isALeafModule: {
      passWhen: length(target.modules) == 0
      message: 'Modules under modules/ must not compose other modules.'
    }
  }
}
```

Each named assertion supplies exactly one of:

- **`passWhen`** — a boolean condition. The assertion passes only when the condition is true.
- **`failOn`** — a collection of offending facts. The assertion passes when the collection is
  **empty**, and when it is not, every element is reported by source location. `failOn` is about
  emptiness, not truthiness: a non-empty collection is a failure even if its contents are falsy.

`message` explains what to do about a failure and is reported alongside it.

The two forms answer different questions. `passWhen` states a property the target must have;
`failOn` enumerates the specific declarations that broke a rule, which is what makes a policy
failure actionable rather than merely true.

A test declaring a non-empty `assertions` object runs **those** assertions only; the target's own
`assert` statements are not evaluated and its parameters are not required. Omitting `assertions`
preserves the target-owned behaviour above. An `assertions` object that is present but empty is an
error, because it neither asserts anything nor falls back to anything.

### The `target` symbol

Inside `assertions`, `target` is a typed, read-only symbol describing the compiler's view of the
selected file. It is not a global Bicep keyword and exists only in this scope.

| Property | Description |
|----------|-------------|
| `target.resources` | Resources the file declares, including nested ones |
| `target.modules` | Module declarations in the file |
| `target.imports` | Compile-time `import` statements in the file |
| `target.withModules` | The same three collections for the file **plus every module it transitively reaches** |

Each resource carries `name`, `type` (without the API version), `existing`, `file` and `line`. Each
module carries `name`, `path` (as written), `resolvedFile`, `file` and `line`. Each import carries
`path`, `resolvedFile`, `symbols`, `wildcard`, `file` and `line`.

`file` and `resolvedFile` are relative to the selector root, not the working directory, so a policy
phrased in repository-relative terms means the same thing no matter where the CLI was invoked from.

These are **source facts**. Conditions are not evaluated and loops are not expanded: a resource
declared with `if (...)` or `for (...)` contributes exactly one fact, because the question being
asked is what the file declares, not what a particular deployment would create.

`target` has no additional properties, so a misspelling such as `target.resourcez` is a compile
error rather than a silently empty result.

Assertion expressions are ordinary Bicep, so the usual functions (`filter`, `map`, `contains`,
`length`, `startsWith`, `union`, …) all apply, and they may reference variables declared in the test
file.

An assertion that cannot be evaluated — indexing past the end of a collection, for example — fails
and reports why. It never passes by default, and it does not prevent the assertions beside it from
being judged.

When comparing `file` or `resolvedFile` against a directory, include the trailing separator
(`startsWith(r.file, 'sql/')`), since paths are compared as text: `'sql'` alone would also match
`sqlbackup/`. Paths always use `/` separators and are normalized, so the same policy means the same
thing on every platform.

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
        ],
        "failures": [
          {
            "name": "nameWithinLengthLimit",
            "message": null,
            "error": null,
            "violations": []
          }
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

Test-owned assertions populate the same `failures` array with their message and the source locations
that violated them, so a host does not have to parse console text to act on a policy failure:

```console
$ bicep test source-policy-failing.biceptest --output-format json
{
  "version": "1.0",
  "mode": "run",
  "cases": [
    {
      "caseId": "source-policy-failing.biceptest#forbidStorageAccounts#modules/blobStorage.bicep",
      "testFile": "source-policy-failing.biceptest",
      "testName": "forbidStorageAccounts",
      "target": "modules/blobStorage.bicep",
      "status": "failed",
      "error": null,
      "assertions": {
        "total": 1,
        "failed": 1,
        "failedNames": [
          "noStorageAccounts"
        ],
        "failures": [
          {
            "name": "noStorageAccounts",
            "message": "Storage accounts must be created by the platform team, not by service modules.",
            "error": null,
            "violations": [
              "blobStorage.bicep(12): storageAccount"
            ]
          }
        ]
      }
    }
  ],
  "summary": {
    "total": 2,
    "passed": 0,
    "failed": 2,
    "skipped": 0
  }
}
```

(The second case is elided above for brevity; the real document contains one case per target.)

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
- `failures` describes each failed assertion. `violations` holds selector-relative source locations
  drawn from the target's own declarations; it is empty for target-owned `assert` statements, which
  have no offending-fact collection.
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
| `app.bicep` | A composition entrypoint that declares no resources of its own |
| `source-policy.biceptest` | Test-owned source policies, including a `withModules` query |
| `source-policy-failing.biceptest` | A source policy that is violated on purpose |

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

Source policies assert about the targets without supplying any parameters, because nothing is
evaluated:

```console
$ bicep test source-policy.biceptest
[✓] Evaluation moduleSourcePolicy (modules/blobStorage.bicep) Passed!
[✓] Evaluation moduleSourcePolicy (modules/fileStorage.bicep) Passed!
[✓] Evaluation compositionPolicy (app.bicep) Passed!
All 3 evaluations passed!
```

When a source policy is violated, the failure names the declarations responsible:

```console
$ bicep test source-policy-failing.biceptest
[✗] Evaluation forbidStorageAccounts (modules/blobStorage.bicep) Failed at 1 / 1 assertions!
	[✗] Assertion noStorageAccounts failed!
		Storage accounts must be created by the platform team, not by service modules.
		blobStorage.bicep(12): storageAccount
[✗] Evaluation forbidStorageAccounts (modules/fileStorage.bicep) Failed at 1 / 1 assertions!
	[✗] Assertion noStorageAccounts failed!
		Storage accounts must be created by the platform team, not by service modules.
		fileStorage.bicep(12): storageAccount
Evaluation Summary: Failure!
Total: 2 - Success: 0 - Skipped: 0 - Failed: 2
```

The locations are relative to the selector root (`modules`), which is the frame of reference the
policy was written in.

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
[✗] Evaluation source-policy-failing.biceptest: forbidStorageAccounts (modules/blobStorage.bicep) Failed at 1 / 1 assertions!
	[✗] Assertion noStorageAccounts failed!
		Storage accounts must be created by the platform team, not by service modules.
		blobStorage.bicep(12): storageAccount
[✗] Evaluation source-policy-failing.biceptest: forbidStorageAccounts (modules/fileStorage.bicep) Failed at 1 / 1 assertions!
	[✗] Assertion noStorageAccounts failed!
		Storage accounts must be created by the platform team, not by service modules.
		fileStorage.bicep(12): storageAccount
[✓] Evaluation source-policy.biceptest: moduleSourcePolicy (modules/blobStorage.bicep) Passed!
[✓] Evaluation source-policy.biceptest: moduleSourcePolicy (modules/fileStorage.bicep) Passed!
[✓] Evaluation source-policy.biceptest: compositionPolicy (app.bicep) Passed!
[✗] Evaluation storage-failing.biceptest: prefixTooLong (storage.bicep) Failed at 1 / 2 assertions!
	[✗] Assertion nameWithinLengthLimit failed!
[✓] Evaluation storage.biceptest: validPrefix (storage.bicep) Passed!
[✓] Evaluation storage.biceptest: prefixAtLengthLimit (storage.bicep) Passed!
Evaluation Summary: Failure!
Total: 10 - Success: 7 - Skipped: 0 - Failed: 3
```

The command exits with code `1` because `storage-failing.biceptest` and
`source-policy-failing.biceptest` are expected to fail. The failure of one file does not stop the
others from running, and one summary reports the aggregate.

Supplying a file that is neither `.bicep` nor `.biceptest` is rejected:

```console
$ bicep test bicepconfig.json
The specified input "...\bicepconfig.json" was not recognized as a Bicep or Bicep test file. Valid files must use either the .bicep or .biceptest extension.
```

## Current limitations

- Parameter values are written inline in the test declaration, and the same values apply to every target a test selects.
- Test-owned assertions query source facts only. Evaluated values — what a target computes for a particular set of inputs — are not yet available to them.
- Tests evaluate templates offline. They do not deploy resources, call Azure, or validate authorization.

For background and ongoing discussion, see [Bicep Experimental Test Framework](https://github.com/Azure/bicep/issues/11967).
