// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
//
// A helper that is not a deployable module on its own. The naming policy test excludes it,
// which is why the exclude pattern exists in `naming.biceptest`.

@export()
func storageAccountName(namePrefix string, suffix string) string => toLower('${namePrefix}${suffix}')
