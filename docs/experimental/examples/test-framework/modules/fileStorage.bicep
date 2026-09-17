// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

@description('Prefix used to build the storage account name.')
param namePrefix string

@description('Location for the storage account.')
param location string = resourceGroup().location

var storageAccountName = toLower('${namePrefix}files')

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Premium_LRS'
  }
  kind: 'FileStorage'
}

output storageAccountName string = storageAccount.name

assert nameIsLowerCase = storageAccountName == toLower(storageAccountName)
assert nameWithinLengthLimit = length(storageAccountName) <= 24
