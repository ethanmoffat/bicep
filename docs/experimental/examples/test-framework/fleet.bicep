// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
//
// A target whose deployed shape depends on its inputs: a loop, a condition and two calls to the
// same module. Reading its source tells you what it may deploy; evaluating it for a specific case
// tells you what it actually would.

@description('Environment this fleet belongs to.')
param environment string

@description('Regions the fleet spans. One data account is deployed per region.')
param regions string[]

@description('Whether the backup account is deployed.')
param enableBackup bool = false

var prefix = toLower('fleet${environment}')

resource dataAccounts 'Microsoft.Storage/storageAccounts@2023-01-01' = [for (region, index) in regions: {
  name: '${prefix}data${index}'
  location: region
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
}]

resource backupAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = if (enableBackup) {
  name: '${prefix}backup'
  location: regions[0]
  sku: {
    name: 'Standard_GRS'
  }
  kind: 'StorageV2'
}

module primaryStamp 'fleet/regionStamp.bicep' = {
  name: 'primaryStamp'
  params: {
    prefix: prefix
    region: regions[0]
    role: 'primary'
  }
}

module secondaryStamp 'fleet/regionStamp.bicep' = {
  name: 'secondaryStamp'
  params: {
    prefix: prefix
    region: regions[1]
    role: 'secondary'
  }
}

output primarySiteName string = primaryStamp.outputs.siteName
output dataAccountCount int = length(regions)
