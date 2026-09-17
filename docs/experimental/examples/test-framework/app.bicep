// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
//
// A composition entrypoint. It declares no resources of its own, which is what the
// `withModules` policy in `source-policy.biceptest` exists to demonstrate.

@description('Prefix used to build the storage account names.')
param namePrefix string

@description('Location for the storage accounts.')
param location string = resourceGroup().location

module blob 'modules/blobStorage.bicep' = {
  name: 'blob'
  params: {
    namePrefix: namePrefix
    location: location
  }
}

module files 'modules/fileStorage.bicep' = {
  name: 'files'
  params: {
    namePrefix: namePrefix
    location: location
  }
}
