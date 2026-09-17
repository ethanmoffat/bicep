// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
//
// A template whose behavior depends on ambient deployment context rather than on parameters.

@description('Location the resources are created in. Defaults to the resource group location.')
param location string = resourceGroup().location

var approvedRegions = [
  'eastus'
  'westus2'
]

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: 'stg${uniqueString(resourceGroup().id)}'
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
}

output accountLocation string = storageAccount.location

assert locationIsApproved = contains(approvedRegions, location)
